// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="DnsCliHostFixture.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestPlatform.TestHost;
using Xunit;

namespace Dns.UnitTests.Integration;

public sealed class DnsCliHostFixture : IAsyncLifetime, IDisposable
{
    private const string ZoneSuffix = ".integration.test";

    private readonly ConcurrentQueue<string>    _logLines = new();
    private readonly TaskCompletionSource<bool> _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Process       _process;
    private Task          _stdoutTask;
    private Task          _stderrTask;
    private string        _configPath;
    private DirectoryInfo _workingDirectory;

    public IPEndPoint DnsEndpoint { get; private set; }

    internal DnsQueryClient Client { get; private set; }

    public string[] Logs => _logLines.ToArray();

    public string BuildHostName(string hostPrefix)
    {
        if (string.IsNullOrWhiteSpace(hostPrefix))
        {
            throw new ArgumentException("Host prefix is required.", nameof(hostPrefix));
        }

        if (hostPrefix.EndsWith(ZoneSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return hostPrefix;
        }

        return $"{hostPrefix}{ZoneSuffix}";
    }

    public async Task InitializeAsync()
    {
        ValidateArtifacts();

        var dnsPort  = GetAvailableUdpPort();
        var httpPort = GetAvailableTcpPort();

        DnsEndpoint = new(IPAddress.Loopback, dnsPort);

        PrepareWorkingDirectory();
        CopyZoneFile();
        WriteConfigFile(dnsPort, httpPort);

        StartProcess();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await WaitForReadyAsync(timeoutCts.Token).ConfigureAwait(false);

        Client = new(DnsEndpoint);
    }

    public async Task DisposeAsync()
    {
        await StopProcessAsync().ConfigureAwait(false);
        CleanupWorkingDirectory();
    }

    public void Dispose()
    {
        StopProcessAsync().GetAwaiter().GetResult();
        CleanupWorkingDirectory();
    }

    private void ValidateArtifacts()
    {
        if (!File.Exists(TestProjectPaths.DnsCliDllPath))
        {
            throw new FileNotFoundException("dns-cli binary not found. Run dotnet build before executing the integration tests.", TestProjectPaths.DnsCliDllPath);
        }

        if (!File.Exists(GetTemplatePath()))
        {
            throw new FileNotFoundException("Integration configuration template is missing.", GetTemplatePath());
        }

        if (!File.Exists(GetZoneSourcePath()))
        {
            throw new FileNotFoundException("Integration zone data file is missing.", GetZoneSourcePath());
        }
    }

    private void PrepareWorkingDirectory()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"dns-cli-tests-{Guid.NewGuid():N}");
        _workingDirectory = Directory.CreateDirectory(tempDirectory);
    }

    private void CopyZoneFile()
    {
        var destination = Path.Combine(_workingDirectory.FullName, "machineinfo.csv");
        File.Copy(GetZoneSourcePath(), destination, overwrite: true);
    }

    private void WriteConfigFile(int dnsPort, int httpPort)
    {
        var template     = File.ReadAllText(GetTemplatePath());
        var zoneFilePath = Path.Combine(_workingDirectory.FullName, "machineinfo.csv");

        template = template.Replace("{{DNS_PORT}}", dnsPort.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        template = template.Replace("{{HTTP_PORT}}", httpPort.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        template = template.Replace("{{ZONE_SUFFIX}}", ZoneSuffix, StringComparison.Ordinal);
        template = template.Replace("{{ZONE_FILE}}", JsonEncodedText.Encode(zoneFilePath).ToString(), StringComparison.Ordinal);

        _configPath = Path.Combine(_workingDirectory.FullName, "appsettings.json");
        File.WriteAllText(_configPath, template, Encoding.UTF8);
    }

    private void StartProcess()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName               = "dotnet",
            Arguments              = $"\"{TestProjectPaths.DnsCliDllPath}\" --appsettings=\"{_configPath}\"",
            WorkingDirectory       = Path.GetDirectoryName(_configPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dns-cli.");
        Console.WriteLine($"Start info: {startInfo.FileName} {startInfo.Arguments}");
        Console.WriteLine($"{File.ReadAllText(_configPath)}");

        if (_process.HasExited)
        {
            throw new InvalidOperationException("dns-cli exited immediately after start.");
        }

        _process.EnableRaisingEvents = true;
        _process.Exited += (_, __) =>
        {
            if (!_readyTcs.Task.IsCompleted)
            {
                _readyTcs.TrySetException(new InvalidOperationException("dns-cli exited before it signaled readiness."));
            }
        };

        _stdoutTask = Task.Run(() => PumpStreamAsync(_process.StandardOutput, "[out]"));
        _stderrTask = Task.Run(() => PumpStreamAsync(_process.StandardError, "[err]"));
    }

    private async Task PumpStreamAsync(StreamReader reader, string prefix)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            var formatted = $"{prefix} {line}";
            _logLines.Enqueue(formatted);

            if (line.IndexOf("Zone reloaded", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _readyTcs.TrySetResult(true);
            }
            Console.WriteLine($"{prefix} {line}");
        }
    }

    private async Task WaitForReadyAsync(CancellationToken cancellationToken)
    {
        var completed = await Task.WhenAny(_readyTcs.Task, Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)).ConfigureAwait(false);
        if (completed != _readyTcs.Task)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("dns-cli did not emit a readiness signal.");
        }

        await _readyTcs.Task.ConfigureAwait(false);
    }

    private async Task StopProcessAsync()
    {
        if (_process == null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }

        try
        {
            await _process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
        }

        if (_stdoutTask != null)
        {
            try
            {
                await _stdoutTask.ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        }

        if (_stderrTask != null)
        {
            try
            {
                await _stderrTask.ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        }

        _process.Dispose();
        _process    = null;
        _stdoutTask = null;
        _stderrTask = null;
    }

    private void CleanupWorkingDirectory()
    {
        try
        {
            if (_workingDirectory != null && _workingDirectory.Exists)
            {
                _workingDirectory.Delete(recursive: true);
            }
        }
        catch
        {
            // ignored
        }
    }

    private static int GetAvailableUdpPort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return (((IPEndPoint)socket.LocalEndPoint)!).Port;
    }

    private static int GetAvailableTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string GetTemplatePath()
        => Path.Combine(TestProjectPaths.TestDataDirectory, "appsettings.template.json");

    private static string GetZoneSourcePath()
        => Path.Combine(TestProjectPaths.TestDataDirectory, "Zones", "integration_machineinfo.csv");
}