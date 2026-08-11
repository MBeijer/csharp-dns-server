// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="SecondaryZoneProvider.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dns.Config;
using Dns.Contracts;
using Dns.Db.Models.EntityFramework.Enums;
using Dns.Models;
using Dns.Models.Enums;
using Dns.RDataTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dns.ZoneProvider.Secondary;

public sealed class SecondaryZoneProvider(
	ILogger<SecondaryZoneProvider> logger,
	IDnsResolver resolver,
	IOptions<ServerOptions> serverOptions
) : BaseZoneProvider(resolver)
{
	private static readonly JsonSerializerOptions CacheSerializerOptions = new() { WriteIndented = true, };

	private readonly Dictionary<string, Zone> _zones = new(StringComparer.OrdinalIgnoreCase);
	private          CancellationTokenSource  _cancellationTokenSource;
	private          SecondarySyncOptions     _settings;
	private          Task                     _runningTask;

	public int ResolverPriority => 100;

	public override void Initialize(ZoneOptions zoneOptions)
	{
		_settings = serverOptions.Value.SecondarySync;
		if (!_settings.Enabled)
			throw new InvalidOperationException("Secondary synchronization is not enabled.");
		if (string.IsNullOrWhiteSpace(_settings.Master))
			throw new InvalidOperationException("Secondary synchronization requires a master endpoint.");

		base.Initialize(zoneOptions);
	}

	public override void Start(CancellationToken ct)
	{
		_cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
		_runningTask = Task.Run(() => RunAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
	}

	public override void Dispose()
	{
		_cancellationTokenSource?.Cancel();
		if (_runningTask != null)
			try
			{
				_runningTask.Wait(TimeSpan.FromSeconds(5));
			}
			catch (AggregateException ex)
			{
				ex.Handle(inner => inner is OperationCanceledException);
			}

		_cancellationTokenSource?.Dispose();
	}

	private async Task RunAsync(CancellationToken cancellationToken)
	{
		await LoadCacheAsync(cancellationToken).ConfigureAwait(false);

		var reconnectDelay = TimeSpan.FromSeconds(Math.Max(1, _settings.ReconnectDelaySeconds));
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				await SubscribeToCatalogAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex) when (ex is IOException or SocketException or InvalidDataException)
			{
				logger.LogWarning(ex, "Secondary catalog connection to {Master} was lost", _settings.Master);
			}

			try
			{
				await Task.Delay(reconnectDelay, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}
	}

	private async Task SubscribeToCatalogAsync(CancellationToken cancellationToken)
	{
		var       endpoint = ParseEndpoint(_settings.Master);
		using var client   = new TcpClient();
		await client.ConnectAsync(endpoint.Host, endpoint.Port, cancellationToken).ConfigureAwait(false);
		var stream = client.GetStream();

		var request = CreateQuery(DnsServer.CatalogZoneName, ResourceType.AXFR);
		await WriteDnsMessageAsync(stream, request, cancellationToken).ConfigureAwait(false);
		logger.LogInformation("Connected to primary catalog stream at {Master}", _settings.Master);

		while (!cancellationToken.IsCancellationRequested)
		{
			var response = await ReadDnsMessageAsync(stream, cancellationToken).ConfigureAwait(false);
			if (response == null) throw new IOException("The primary closed the catalog stream.");
			if (response.QueryIdentifier != request.QueryIdentifier)
				throw new InvalidDataException("The catalog response has an unexpected query identifier.");
			if (response.RCode != (byte)RCode.NOERROR)
				throw new InvalidDataException(
					$"The primary rejected the catalog subscription with {(RCode)response.RCode}."
				);

			await ApplyCatalogAsync(response, endpoint, cancellationToken).ConfigureAwait(false);
		}
	}

	private async Task ApplyCatalogAsync(
		DnsMessage catalog,
		MasterEndpoint endpoint,
		CancellationToken cancellationToken
	)
	{
		var soaRecords = catalog.Answers.Where(record => record.Type == ResourceType.SOA && record.RData is SOARData)
		                        .ToList();
		if (soaRecords.Count < 2 || !IsCatalogSoa(soaRecords[0]) || !IsCatalogSoa(soaRecords[^1]))
			throw new InvalidDataException("The primary returned an invalid catalog snapshot.");

		var entries = soaRecords.Skip(1)
		                        .SkipLast(1)
		                        .GroupBy(record => CanonicalName(record.Name), StringComparer.OrdinalIgnoreCase)
		                        .ToDictionary(
			                        group => group.Key,
			                        group => ((SOARData)group.Last().RData).Serial,
			                        StringComparer.OrdinalIgnoreCase
		                        );

		var changed = false;
		foreach (var entry in entries)
		{
			if (_zones.TryGetValue(entry.Key, out var current) && current.Serial == entry.Value) continue;

			try
			{
				var transferred = await TransferZoneAsync(endpoint, entry.Key, cancellationToken).ConfigureAwait(false);
				_zones[entry.Key] = transferred;
				changed           = true;
				logger.LogInformation(
					"Transferred secondary zone {Zone} at serial {Serial} from {Master}",
					entry.Key,
					transferred.Serial,
					_settings.Master
				);
			}
			catch (Exception ex) when (ex is IOException or SocketException or InvalidDataException)
			{
				logger.LogWarning(
					ex,
					"Unable to transfer secondary zone {Zone} from {Master}",
					entry.Key,
					_settings.Master
				);
			}
		}

		foreach (var removedZone in _zones.Keys.Where(zoneName => !entries.ContainsKey(zoneName)).ToList())
		{
			_zones.Remove(removedZone);
			changed = true;
			logger.LogInformation(
				"Removed secondary zone {Zone} because it is no longer in the primary catalog",
				removedZone
			);
		}

		if (!changed) return;

		var snapshot = _zones.Values.OrderBy(zone => zone.Suffix, StringComparer.OrdinalIgnoreCase).ToList();
		Notify(snapshot);
		try
		{
			await SaveCacheAsync(snapshot, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			logger.LogWarning(ex, "Unable to save secondary zone cache {CacheFile}", _settings.CacheFile);
		}
	}

	private static bool IsCatalogSoa(ResourceRecord record) =>
		string.Equals(CanonicalName(record.Name), DnsServer.CatalogZoneName, StringComparison.OrdinalIgnoreCase);

	private static async Task<Zone> TransferZoneAsync(
		MasterEndpoint endpoint,
		string zoneName,
		CancellationToken cancellationToken
	)
	{
		using var client = new TcpClient();
		await client.ConnectAsync(endpoint.Host, endpoint.Port, cancellationToken).ConfigureAwait(false);
		var stream  = client.GetStream();
		var request = CreateQuery(zoneName, ResourceType.AXFR);
		await WriteDnsMessageAsync(stream, request, cancellationToken).ConfigureAwait(false);

		var records        = new List<ResourceRecord>();
		var openingSoaSeen = false;
		while (!cancellationToken.IsCancellationRequested)
		{
			var response = await ReadDnsMessageAsync(stream, cancellationToken).ConfigureAwait(false) ??
			               throw new InvalidDataException($"AXFR for '{zoneName}' ended before the closing SOA.");
			if (response.QueryIdentifier != request.QueryIdentifier)
				throw new InvalidDataException($"AXFR for '{zoneName}' has an unexpected query identifier.");
			if (response.RCode != (byte)RCode.NOERROR)
				throw new InvalidDataException($"AXFR for '{zoneName}' failed with {(RCode)response.RCode}.");

			foreach (var record in response.Answers)
			{
				records.Add(record);
				if (record.Type != ResourceType.SOA ||
				    !string.Equals(CanonicalName(record.Name), zoneName, StringComparison.OrdinalIgnoreCase))
					continue;

				if (!openingSoaSeen)
				{
					openingSoaSeen = true;
					continue;
				}

				return BuildZone(zoneName, records);
			}
		}

		throw new OperationCanceledException(cancellationToken);
	}

	private static Zone BuildZone(string zoneName, IReadOnlyList<ResourceRecord> transferRecords)
	{
		var openingSoa = transferRecords.FirstOrDefault(record => record.Type == ResourceType.SOA)?.RData as SOARData ??
		                 throw new InvalidDataException($"AXFR for '{zoneName}' does not start with an SOA record.");
		var groupedRecords = new Dictionary<(string Host, ResourceType Type, ResourceClass Class), ZoneRecord>();

		foreach (var record in transferRecords.Skip(1).SkipLast(1))
		{
			var address = GetRecordData(record);
			if (address == null) continue;

			var host = GetRelativeHost(record.Name, zoneName);
			var key  = (host, record.Type, record.Class);
			if (!groupedRecords.TryGetValue(key, out var zoneRecord))
			{
				zoneRecord = new()
				{
					Host = host, Type = record.Type, Class = record.Class, Addresses = [],
				};
				groupedRecords[key] = zoneRecord;
			}

			zoneRecord.Addresses.Add(address);
			zoneRecord.Count = zoneRecord.Addresses.Count;
		}

		groupedRecords[(string.Empty, ResourceType.SOA, ResourceClass.IN)] = new()
		{
			Host      = string.Empty,
			Type      = ResourceType.SOA,
			Class     = ResourceClass.IN,
			Addresses = [openingSoa.PrimaryNameServer, openingSoa.ResponsibleAuthoritativeMailbox],
			Count     = 2,
		};

		var zone = new Zone { Suffix = zoneName, Serial = openingSoa.Serial };
		zone.Initialize(groupedRecords.Values);
		return zone;
	}

	private static string GetRecordData(ResourceRecord record) =>
		record.RData switch
		{
			ANameRData address       => address.Address.ToString(),
			CNameRData cname         => cname.Name,
			NSRData ns               => ns.Name,
			MXRData mx               => $"{mx.Preference} {mx.Name}",
			TXTRData txt             => txt.Name,
			DomainNamePointRData ptr => ptr.Name,
			_                        => null,
		};

	private static string GetRelativeHost(string ownerName, string zoneName)
	{
		var owner = CanonicalName(ownerName);
		if (string.Equals(owner, zoneName, StringComparison.OrdinalIgnoreCase)) return string.Empty;

		var suffix = $".{zoneName}";
		return owner.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? owner[..^suffix.Length] : owner;
	}

	private async Task LoadCacheAsync(CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(_settings.CacheFile)) return;

		var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(_settings.CacheFile));
		if (!File.Exists(path)) return;

		try
		{
			var        stream = File.OpenRead(path);
			List<Zone> cachedZones;
			await using (stream.ConfigureAwait(false))
				cachedZones = await JsonSerializer.DeserializeAsync<List<Zone>>(
					                                  utf8Json: stream,
					                                  options: CacheSerializerOptions,
					                                  cancellationToken: cancellationToken
				                                  )
				                                  .ConfigureAwait(false);
			foreach (var zone in cachedZones ?? [])
				if (!string.IsNullOrWhiteSpace(zone.Suffix))
					_zones[CanonicalName(zone.Suffix)] = zone;

			if (_zones.Count > 0) Notify(_zones.Values.ToList());
		}
		catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
		{
			logger.LogWarning(ex, "Unable to load secondary zone cache {CacheFile}", path);
		}
	}

	private async Task SaveCacheAsync(IReadOnlyCollection<Zone> zones, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(_settings.CacheFile)) return;

		var path      = Path.GetFullPath(Environment.ExpandEnvironmentVariables(_settings.CacheFile));
		var directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

		var temporaryPath = $"{path}.tmp";
		var stream        = File.Create(temporaryPath);
		await using (stream.ConfigureAwait(false))
			await JsonSerializer.SerializeAsync(stream, zones, CacheSerializerOptions, cancellationToken)
			                    .ConfigureAwait(false);

		File.Move(temporaryPath, path, true);
	}

	private static DnsMessage CreateQuery(string name, ResourceType type)
	{
		var request = new DnsMessage
		{
			QueryIdentifier = (ushort)Random.Shared.Next(ushort.MinValue, ushort.MaxValue + 1), QuestionCount = 1,
		};
		request.Questions.Add(new(name, type, ResourceClass.IN));
		return request;
	}

	private static async Task WriteDnsMessageAsync(
		NetworkStream stream,
		DnsMessage message,
		CancellationToken cancellationToken
	)
	{
		using var payloadStream = new MemoryStream();
		message.WriteToStream(payloadStream);
		if (payloadStream.Length > ushort.MaxValue)
			throw new InvalidDataException("A DNS-over-TCP message cannot exceed 65535 bytes.");

		var prefix = new byte[2];
		BinaryPrimitives.WriteUInt16BigEndian(prefix, (ushort)payloadStream.Length);
		await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
		await stream.WriteAsync(payloadStream.GetBuffer().AsMemory(0, (int)payloadStream.Length), cancellationToken)
		            .ConfigureAwait(false);
	}

	private static async Task<DnsMessage> ReadDnsMessageAsync(NetworkStream stream, CancellationToken cancellationToken)
	{
		var prefix       = new byte[2];
		var prefixLength = await ReadExactAsync(stream, prefix, cancellationToken).ConfigureAwait(false);
		if (prefixLength == 0) return null;
		if (prefixLength != prefix.Length) throw new InvalidDataException("Incomplete DNS-over-TCP length prefix.");

		var payload = new byte[BinaryPrimitives.ReadUInt16BigEndian(prefix)];
		if (payload.Length == 0) throw new InvalidDataException("A DNS-over-TCP message cannot be empty.");
		if (await ReadExactAsync(stream, payload, cancellationToken).ConfigureAwait(false) != payload.Length)
			throw new InvalidDataException("Incomplete DNS-over-TCP message.");

		return DnsMessage.TryParse(payload, out var message)
			? message
			: throw new InvalidDataException("Unable to parse DNS-over-TCP message.");
	}

	private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
	{
		var offset = 0;
		while (offset < buffer.Length)
		{
			var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken)
			                       .ConfigureAwait(false);
			if (read == 0) break;
			offset += read;
		}

		return offset;
	}

	private static MasterEndpoint ParseEndpoint(string value)
	{
		var trimmed = value.Trim();
		if (trimmed.StartsWith("[", StringComparison.Ordinal))
		{
			var closingBracket = trimmed.IndexOf(']');
			if (closingBracket <= 1) throw new InvalidDataException($"Invalid master endpoint '{value}'.");

			var host = trimmed[1..closingBracket];
			if (closingBracket == trimmed.Length - 1) return new(host, 53);
			if (trimmed[closingBracket + 1] != ':' ||
			    !ushort.TryParse(trimmed[(closingBracket + 2)..], out var bracketedPort))
				throw new InvalidDataException($"Invalid master endpoint '{value}'.");

			return new(host, bracketedPort);
		}

		if (System.Net.IPAddress.TryParse(trimmed, out _)) return new(trimmed, 53);

		var separator = trimmed.LastIndexOf(':');
		if (separator > 0 && ushort.TryParse(trimmed[(separator + 1)..], out var port))
			return new(trimmed[..separator], port);

		return new(trimmed, 53);
	}

	private static string CanonicalName(string name) => name?.Trim().Trim('.') ?? string.Empty;

	private sealed record MasterEndpoint(string Host, ushort Port);
}