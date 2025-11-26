// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="BindZoneProvider.cs" >
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Dns.Config;
using Dns.Contracts;
using Dns.Db.Models.EntityFramework.Enums;
using Dns.Models;
using Microsoft.Extensions.Logging;

namespace Dns.ZoneProvider.Bind;

/// <summary>
/// Zone provider that parses BIND-style forward zone files and publishes address records to SmartZoneResolver.
/// </summary>
public class BindZoneProvider(ILogger<BindZoneProvider> logger, IDnsResolver dnsResolver) : FileWatcherZoneProvider(dnsResolver)
{
    /// <summary>Initialize ZoneProvider</summary>
    /// <param name="zoneOptions">ZoneProvider Configuration Section</param>
    public override void Initialize(ZoneOptions zoneOptions)
    {
        Zone.Suffix = zoneOptions.Name;

        base.Initialize(zoneOptions);
    }

    public override Zone GenerateZone()
    {
        try
        {
            var parser  = new ZoneFileParser(Filename, Zone.Suffix);
            var records = parser.Parse();

            var soaRecord = records.FirstOrDefault(r => r.Type == ResourceType.SOA);

            Zone.Initialize(records);

            return Zone;
        }
        catch (BindZoneFileException ex)
        {
            logger.LogError(ex, "BIND zone parse error ({Filename}:{LineNumber}): {Message}", Filename, ex.LineNumber, ex.Message);
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Unable to read BIND zone file {Filename}: {Message}", Filename, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Unable to access BIND zone file {Filename}: {Message}", Filename, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while parsing {Filename}: {Message}", Filename, ex.Message);
        }

        return null;
    }

    private sealed class ZoneFileParser
    {
        private readonly string                         _filename;
        private readonly string                         _zoneRoot;
        private readonly string                         _zoneRootSuffix;
        private readonly Dictionary<string, NameRecord> _records = new(StringComparer.OrdinalIgnoreCase);
        private readonly string                         _defaultOrigin;

        private string _currentOrigin;
        private string _lastOwner;
        private bool   _sawSoa;
        private int    _apexNsCount;
        private uint?  _defaultTtl;
        private int    _lastLineNumber;

        public ZoneFileParser(string filename, string zoneSuffix)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filename);

            _filename       = filename;
            _zoneRoot       = NormalizeZoneSuffix(zoneSuffix);
            _zoneRootSuffix = "." + _zoneRoot;
            _defaultOrigin  = _zoneRoot + ".";
            _currentOrigin  = _defaultOrigin;
        }

        public IReadOnlyList<ZoneRecord> Parse()
        {
            using (var reader = new StreamReader(_filename))
            {
                foreach (var line in ReadLogicalLines(reader))
                {
                    _lastLineNumber = line.LineNumber;

                    if (string.IsNullOrWhiteSpace(line.Text))
                    {
                        continue;
                    }

                    if (line.Text.StartsWith("$", StringComparison.Ordinal))
                    {
                        ProcessDirective(line);
                    }
                    else
                    {
                        ProcessRecord(line);
                    }
                }
            }

            if (!_sawSoa)
            {
                throw new BindZoneFileException(_lastLineNumber, "Zone file must contain exactly one SOA record.");
            }

            if (_apexNsCount == 0)
            {
                throw new BindZoneFileException(_lastLineNumber, "Zone file must declare at least one NS record for the zone apex.");
            }

            var zoneRecords = new List<ZoneRecord>();

            foreach (var record in _records.Values)
            {
                if (record.Ipv4Addresses.Count > 0)
                {
                    zoneRecords.Add(new()
                    {
                        Host      = record.Name,
                        Addresses = record.Ipv4Addresses.Select(s => s.ToString()).ToList(),
                        Count     = record.Ipv4Addresses.Count,
                        Class     = ResourceClass.IN,
                        Type      = ResourceType.A,
                    });
                }

                if (record.Ipv6Addresses.Count > 0)
                {
                    zoneRecords.Add(new()
                    {
                        Host      = record.Name,
                        Addresses = record.Ipv6Addresses.Select(s => s.ToString()).ToList(),
                        Count     = record.Ipv6Addresses.Count,
                        Class     = ResourceClass.IN,
                        Type      = ResourceType.AAAA,
                    });
                }
            }

            if (zoneRecords.Count == 0)
            {
                throw new BindZoneFileException(_lastLineNumber, "Zone file did not produce any address records.");
            }

            return zoneRecords;
        }

        private void ProcessDirective(LogicalLine line)
        {
            var tokens    = Tokenize(line.Text, line.LineNumber);
            var directive = tokens[0].ToUpperInvariant();

            switch (directive)
            {
                case "$ORIGIN":
                    if (tokens.Count != 2)
                    {
                        throw new BindZoneFileException(line.LineNumber, "$ORIGIN expects a single domain name argument.");
                    }
                    ApplyOrigin(tokens[1], line.LineNumber);
                    break;
                case "$TTL":
                    if (tokens.Count != 2)
                    {
                        throw new BindZoneFileException(line.LineNumber, "$TTL expects a single value.");
                    }
                    _defaultTtl = ParseTtl(tokens[1], line.LineNumber);
                    break;
                case "$INCLUDE":
                    throw new BindZoneFileException(line.LineNumber, "$INCLUDE is not supported in this build.");
                default:
                    throw new BindZoneFileException(line.LineNumber, string.Format(CultureInfo.InvariantCulture, "Unsupported directive '{0}'.", directive));
            }
        }

        private void ProcessRecord(LogicalLine line)
        {
            var tokens = Tokenize(line.Text, line.LineNumber);
            if (tokens.Count == 0)
            {
                return;
            }

            var    index = 0;
            string owner;

            if (line.OwnerImplicit)
            {
                if (string.IsNullOrEmpty(_lastOwner))
                {
                    throw new BindZoneFileException(line.LineNumber, "Record omitted owner but no previous owner exists.");
                }
                owner = _lastOwner;
            }
            else
            {
                owner           = CanonicalizeOwner(tokens[index++], line.LineNumber);
                _lastOwner = owner;
            }

            var   recordClass = "IN";
            uint? recordTtl   = null;

            while (index < tokens.Count)
            {
                var token = tokens[index];
                if (IsClassToken(token))
                {
                    recordClass = token.ToUpperInvariant();
                    index++;
                    continue;
                }

                if (TryParseTtlToken(token, line.LineNumber, out var ttl))
                {
                    recordTtl = ttl;
                    index++;
                    continue;
                }

                break;
            }

            if (!string.Equals(recordClass, "IN", StringComparison.OrdinalIgnoreCase))
            {
                throw new BindZoneFileException(line.LineNumber, string.Format(CultureInfo.InvariantCulture, "Unsupported class '{0}'.", recordClass));
            }

            if (index >= tokens.Count)
            {
                throw new BindZoneFileException(line.LineNumber, "Record is missing a type token.");
            }

            var typeToken = tokens[index++].ToUpperInvariant();
            var rdata     = tokens.Skip(index).ToList();

            var record = GetOrCreateRecord(owner);

            switch (typeToken)
            {
                case "SOA":
                    ParseSoa(owner, rdata, line.LineNumber);
                    break;
                case "NS":
                    ParseNs(record, rdata, line.LineNumber);
                    break;
                case "A":
                    ParseAddressRecord(record, rdata, line.LineNumber, ResourceType.A);
                    break;
                case "AAAA":
                    ParseAddressRecord(record, rdata, line.LineNumber, ResourceType.AAAA);
                    break;
                case "CNAME":
                    ParseCName(record, rdata, line.LineNumber);
                    break;
                case "MX":
                    ParseMx(record, rdata, line.LineNumber);
                    break;
                case "TXT":
                    ParseTxt(record, rdata, line.LineNumber);
                    break;
                default:
                    throw new BindZoneFileException(line.LineNumber, string.Format(CultureInfo.InvariantCulture, "Record type '{0}' is not supported.", typeToken));
            }

            // TTL currently unused but parsing keeps validation pathways ready.
            if (!recordTtl.HasValue && !_defaultTtl.HasValue)
            {
                throw new BindZoneFileException(line.LineNumber, "Record does not specify a TTL and no default $TTL directive exists.");
            }
        }

        private void ParseSoa(string owner, List<string> rdata, int lineNumber)
        {
            if (_sawSoa)
            {
                throw new BindZoneFileException(lineNumber, "Multiple SOA records detected.");
            }

            if (!string.Equals(owner, _zoneRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new BindZoneFileException(lineNumber, "SOA record must belong to the zone apex.");
            }

            if (rdata.Count < 7)
            {
                throw new BindZoneFileException(lineNumber, "SOA record must include MNAME, RNAME, SERIAL, REFRESH, RETRY, EXPIRE, and MINIMUM fields.");
            }

            CanonicalizeName(rdata[0], lineNumber); // primary name server
            CanonicalizeName(rdata[1], lineNumber); // responsible mailbox

            for (var i = 2; i < 7; i++)
            {
                if (!ulong.TryParse(rdata[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var _))
                {
                    throw new BindZoneFileException(lineNumber, string.Format(CultureInfo.InvariantCulture, "Invalid SOA numeric field '{0}'.", rdata[i]));
                }
            }

            _sawSoa = true;
        }

        private void ParseNs(NameRecord record, List<string> rdata, int lineNumber)
        {
            if (rdata.Count != 1)
            {
                throw new BindZoneFileException(lineNumber, "NS record expects a single target name.");
            }

            CanonicalizeName(rdata[0], lineNumber);
            record.RegisterGenericRecord("NS", lineNumber);

            if (string.Equals(record.Name, _zoneRoot, StringComparison.OrdinalIgnoreCase))
            {
                _apexNsCount++;
            }
        }

        private void ParseMx(NameRecord record, List<string> rdata, int lineNumber)
        {
            if (rdata.Count < 2)
            {
                throw new BindZoneFileException(lineNumber, "MX record expects preference and target host.");
            }

            if (!ushort.TryParse(rdata[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var _))
            {
                throw new BindZoneFileException(lineNumber, "MX preference must be between 0 and 65535.");
            }

            CanonicalizeName(rdata[1], lineNumber);
            record.RegisterGenericRecord("MX", lineNumber);
        }

        private void ParseTxt(NameRecord record, List<string> rdata, int lineNumber)
        {
            if (rdata.Count == 0)
            {
                throw new BindZoneFileException(lineNumber, "TXT record must include at least one string literal.");
            }

            record.RegisterGenericRecord("TXT", lineNumber);
        }

        private void ParseCName(NameRecord record, List<string> rdata, int lineNumber)
        {
            if (rdata.Count != 1)
            {
                throw new BindZoneFileException(lineNumber, "CNAME record expects a single target.");
            }

            var target = CanonicalizeName(rdata[0], lineNumber);
            record.SetCName(target, lineNumber);
        }

        private void ParseAddressRecord(NameRecord record, List<string> rdata, int lineNumber, ResourceType resourceType)
        {
            if (rdata.Count != 1)
            {
                throw new BindZoneFileException(lineNumber, string.Format(CultureInfo.InvariantCulture, "{0} record expects a single address.", resourceType));
            }

            if (!IPAddress.TryParse(rdata[0], out var address))
            {
                throw new BindZoneFileException(lineNumber, string.Format(CultureInfo.InvariantCulture, "'{0}' is not a valid IP address.", rdata[0]));
            }

            if (resourceType == ResourceType.A && address.AddressFamily != AddressFamily.InterNetwork)
            {
                throw new BindZoneFileException(lineNumber, "A record data must be an IPv4 address.");
            }

            if (resourceType == ResourceType.AAAA && address.AddressFamily != AddressFamily.InterNetworkV6)
            {
                throw new BindZoneFileException(lineNumber, "AAAA record data must be an IPv6 address.");
            }

            record.AddAddress(resourceType, address, lineNumber);
        }

        private void ApplyOrigin(string value, int lineNumber)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new BindZoneFileException(lineNumber, "$ORIGIN directive requires a domain name.");
            }

            string newOrigin;
            if (value == "@")
            {
                newOrigin = _defaultOrigin;
            }
            else if (value.EndsWith(".", StringComparison.Ordinal))
            {
                newOrigin = value;
            }
            else
            {
                newOrigin = value + "." + TrimTrailingDot(_currentOrigin);
            }

            var normalized = TrimTrailingDot(newOrigin);
            EnsureWithinZone(normalized, lineNumber);
            _currentOrigin = normalized + ".";
        }

        private NameRecord GetOrCreateRecord(string owner)
        {
            NameRecord record;
            if (!_records.TryGetValue(owner, out record))
            {
                record = new(owner);
                _records.Add(owner, record);
            }

            return record;
        }

        private string CanonicalizeOwner(string token, int lineNumber)
        {
            var canonical = CanonicalizeName(token, lineNumber);
            EnsureWithinZone(canonical, lineNumber);
            return canonical;
        }

        private string CanonicalizeName(string token, int lineNumber)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new BindZoneFileException(lineNumber, "Name token cannot be empty.");
            }

            var input = token.Trim();
            if (input == "@")
            {
                return TrimTrailingDot(_currentOrigin);
            }

            if (input == ".")
            {
                throw new BindZoneFileException(lineNumber, "Root label '.' is not supported in this context.");
            }

            if (input.EndsWith(".", StringComparison.Ordinal))
            {
                return TrimTrailingDot(input);
            }

            return TrimTrailingDot(input + "." + _currentOrigin);
        }

        private void EnsureWithinZone(string fqdn, int lineNumber)
        {
            if (fqdn.Equals(_zoneRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!fqdn.EndsWith(_zoneRootSuffix, StringComparison.OrdinalIgnoreCase))
            {
                throw new BindZoneFileException(lineNumber, string.Format(CultureInfo.InvariantCulture, "Owner '{0}' falls outside of zone '{1}'.", fqdn, _zoneRoot));
            }
        }

        private uint ParseTtl(string token, int lineNumber)
        {
            if (!TryParseTtlToken(token, lineNumber, out var ttl))
            {
                throw new BindZoneFileException(lineNumber, string.Format(CultureInfo.InvariantCulture, "'{0}' is not a valid TTL.", token));
            }

            return ttl;
        }

        private bool TryParseTtlToken(string token, int lineNumber, out uint value)
        {
            value = 0;
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            var index = 0;
            while (index < token.Length && char.IsDigit(token[index]))
            {
                index++;
            }

            if (index == 0)
            {
                return false;
            }

            var numberPart = token.Substring(0, index);
            if (!uint.TryParse(numberPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var magnitude))
            {
                throw new BindZoneFileException(lineNumber, string.Format(CultureInfo.InvariantCulture, "Unable to parse TTL value '{0}'.", token));
            }

            if (index == token.Length)
            {
                value = magnitude;
                return true;
            }

            if (index != token.Length - 1)
            {
                return false;
            }

            var  suffix = char.ToLowerInvariant(token[index]);
            uint multiplier;
            switch (suffix)
            {
                case 's':
                    multiplier = 1;
                    break;
                case 'm':
                    multiplier = 60;
                    break;
                case 'h':
                    multiplier = 3600;
                    break;
                case 'd':
                    multiplier = 86400;
                    break;
                case 'w':
                    multiplier = 604800;
                    break;
                default:
                    return false;
            }

            var total = (ulong)magnitude * multiplier;
            if (total > uint.MaxValue)
            {
                throw new BindZoneFileException(lineNumber, "TTL value is too large.");
            }

            value = (uint)total;
            return true;
        }

        private static List<string> Tokenize(string text, int lineNumber)
        {
            var tokens   = new List<string>();
            var builder  = new StringBuilder();
            var inQuotes = false;
            var escape   = false;

            foreach (var current in text)
            {
                if (escape)
                {
                    builder.Append(current);
                    escape = false;
                    continue;
                }

                if (current == '\\')
                {
                    escape = true;
                    continue;
                }

                if (current == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (!inQuotes && char.IsWhiteSpace(current))
                {
                    if (builder.Length > 0)
                    {
                        tokens.Add(builder.ToString());
                        builder.Clear();
                    }
                    continue;
                }

                builder.Append(current);
            }

            if (escape)
            {
                throw new BindZoneFileException(lineNumber, "Dangling escape sequence in record.");
            }

            if (inQuotes)
            {
                throw new BindZoneFileException(lineNumber, "Unterminated quote detected.");
            }

            if (builder.Length > 0)
            {
                tokens.Add(builder.ToString());
            }

            return tokens;
        }

        private IEnumerable<LogicalLine> ReadLogicalLines(TextReader reader)
        {
            string line;
            var    lineNumber       = 0;
            var    recordStartLine  = 0;
            var    recordHasContent = false;
            var    ownerImplicit    = false;
            var    parenDepth       = 0;
            var    builder          = new StringBuilder();

            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                var sanitized = StripComments(line, lineNumber, out var parenDelta, out var startsWithWhitespace, out var hasContent);

                if (hasContent && !recordHasContent)
                {
                    recordHasContent = true;
                    recordStartLine  = lineNumber;
                    ownerImplicit    = startsWithWhitespace;
                }

                if (hasContent)
                {
                    if (builder.Length > 0)
                    {
                        builder.Append(' ');
                    }

                    builder.Append(sanitized.Trim());
                }

                parenDepth += parenDelta;
                if (parenDepth < 0)
                {
                    throw new BindZoneFileException(lineNumber, "Unmatched ')' detected.");
                }

                if (recordHasContent && parenDepth == 0)
                {
                    yield return new(recordStartLine, builder.ToString(), ownerImplicit);
                    builder.Clear();
                    recordHasContent = false;
                    ownerImplicit    = false;
                }
            }

            if (parenDepth != 0)
            {
                throw new BindZoneFileException(lineNumber, "Unterminated multi-line record detected.");
            }
        }

        private string StripComments(string line, int lineNumber, out int parenDelta, out bool startsWithWhitespace, out bool hasContent)
        {
            var builder  = new StringBuilder();
            var inQuotes = false;
            var escape   = false;
            parenDelta = 0;

            for (var i = 0; i < line.Length; i++)
            {
                var current = line[i];

                if (escape)
                {
                    builder.Append(current);
                    escape = false;
                    continue;
                }

                if (current == '\\')
                {
                    escape = true;
                    builder.Append(current);
                    continue;
                }

                if (!inQuotes && current == ';')
                {
                    break;
                }

                if (current == '"')
                {
                    inQuotes = !inQuotes;
                    builder.Append(current);
                    continue;
                }

                if (!inQuotes && (current == '(' || current == ')'))
                {
                    parenDelta += current == '(' ? 1 : -1;
                    builder.Append(' ');
                    continue;
                }

                builder.Append(current);
            }

            if (escape)
            {
                throw new BindZoneFileException(lineNumber, "Dangling escape sequence inside line.");
            }

            if (inQuotes)
            {
                throw new BindZoneFileException(lineNumber, "Unterminated quote inside line.");
            }

            var sanitized               = builder.ToString();
            var firstNonWhitespaceIndex = -1;
            for (var i = 0; i < sanitized.Length; i++)
            {
                if (!char.IsWhiteSpace(sanitized[i]))
                {
                    firstNonWhitespaceIndex = i;
                    break;
                }
            }

            hasContent           = firstNonWhitespaceIndex >= 0;
            startsWithWhitespace = hasContent && firstNonWhitespaceIndex > 0;

            return sanitized;
        }

        private static string NormalizeZoneSuffix(string zone)
        {
            if (string.IsNullOrWhiteSpace(zone))
            {
                throw new ArgumentException("zone");
            }

            var trimmed = zone.Trim();
            if (trimmed.StartsWith(".", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(1);
            }

            if (trimmed.EndsWith(".", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 1);
            }

            if (string.IsNullOrEmpty(trimmed))
            {
                throw new ArgumentException("zone");
            }

            return trimmed;
        }

        private static string TrimTrailingDot(string value)
        {
            if (value.EndsWith(".", StringComparison.Ordinal))
            {
                return value.Substring(0, value.Length - 1);
            }

            return value;
        }

        private static bool IsClassToken(string token)
        {
            return token.Equals("IN", StringComparison.OrdinalIgnoreCase) ||
                   token.Equals("CH", StringComparison.OrdinalIgnoreCase) ||
                   token.Equals("HS", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class LogicalLine
        {
            public LogicalLine(int lineNumber, string text, bool ownerImplicit)
            {
                LineNumber    = lineNumber;
                Text          = text;
                OwnerImplicit = ownerImplicit;
            }

            public int LineNumber { get; }

            public string Text { get; }

            public bool OwnerImplicit { get; }
        }

        private sealed class NameRecord
        {
            public NameRecord(string name)
            {
                Name = name;
            }

            public string Name { get; }

            public HashSet<IPAddress> Ipv4Addresses { get; } = new();

            public HashSet<IPAddress> Ipv6Addresses { get; } = new();

            public string CNameTarget { get; private set; }

            private bool HasOtherRecords { get; set; }

            public void AddAddress(ResourceType resourceType, IPAddress address, int lineNumber)
            {
                EnsureNotCName(resourceType.ToString(), lineNumber);

                if (resourceType == ResourceType.A)
                {
                    Ipv4Addresses.Add(address);
                }
                else
                {
                    Ipv6Addresses.Add(address);
                }

                HasOtherRecords = true;
            }

            public void RegisterGenericRecord(string recordType, int lineNumber)
            {
                EnsureNotCName(recordType, lineNumber);
                HasOtherRecords = true;
            }

            public void SetCName(string target, int lineNumber)
            {
                if (HasOtherRecords)
                {
                    throw new BindZoneFileException(lineNumber, string.Format(CultureInfo.InvariantCulture, "'{0}' already hosts other records and cannot also be a CNAME.", Name));
                }

                if (CNameTarget != null && !CNameTarget.Equals(target, StringComparison.OrdinalIgnoreCase))
                {
                    throw new BindZoneFileException(lineNumber, string.Format(CultureInfo.InvariantCulture, "Conflicting CNAME definition for '{0}'.", Name));
                }

                CNameTarget = target;
            }

            private void EnsureNotCName(string recordType, int lineNumber)
            {
                if (CNameTarget != null)
                {
                    throw new BindZoneFileException(lineNumber, string.Format(CultureInfo.InvariantCulture, "'{0}' is a CNAME and cannot host {1} records.", Name, recordType));
                }
            }
        }
    }

    private sealed class BindZoneFileException : Exception
    {
        public BindZoneFileException(int lineNumber, string message)
            : base(message)
        {
            LineNumber = lineNumber;
        }

        public int LineNumber { get; }
    }
}