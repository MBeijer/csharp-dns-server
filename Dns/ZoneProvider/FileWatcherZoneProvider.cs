// // //-------------------------------------------------------------------------------------------------
// // // <copyright file="ZoneProvider.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dns.Config;
using Dns.Contracts;

namespace Dns.ZoneProvider;

public abstract class FileWatcherZoneProvider(IDnsResolver resolver) : BaseZoneProvider(resolver)
{
    public delegate void FileWatcherDelegate(object sender, FileSystemEventArgs e);

    public event FileWatcherDelegate OnCreated    = delegate {};
    public event FileWatcherDelegate OnDeleted    = delegate {};
    public event FileWatcherDelegate OnRenamed    = delegate {};
    public event FileWatcherDelegate OnChanged    = delegate {};
    public event FileWatcherDelegate OnSettlement = delegate {};

    private FileSystemWatcher _fileWatcher;
    private Timer             _timer;

    protected abstract Zone GenerateZone();

    /// <summary>Timespan between last file change and zone generation</summary>
    private TimeSpan FileSettlementPeriod { get; set; } = TimeSpan.FromSeconds(10);

    protected string Filename { get; private set; }

    public override void Initialize(ZoneOptions zoneOptions)
    {
        var fileWatcherConfig = zoneOptions.ProviderSettings as FileWatcherZoneProviderSettings;

        var filename = fileWatcherConfig!.FileName;

        ArgumentException.ThrowIfNullOrEmpty(filename, "filename");

        filename = Environment.ExpandEnvironmentVariables(filename);
        filename = Path.GetFullPath(filename);

        if (!File.Exists(filename))
            throw new FileNotFoundException("filename not found", filename);


        var directory = Path.GetDirectoryName(filename);
        var fileNameFilter = Path.GetFileName(filename);

        Filename = filename;
        _fileWatcher = new(directory, fileNameFilter);

        _fileWatcher.Created += (s, e) => OnCreated(s, e);
        _fileWatcher.Changed += (s, e) => OnChanged(s, e);
        _fileWatcher.Renamed += (s, e) => OnRenamed(s, e);
        _fileWatcher.Deleted += (s, e) => OnDeleted(s, e);

        _timer = new(OnTimer);

        _fileWatcher.Created += FileChange;
        _fileWatcher.Changed += FileChange;
        _fileWatcher.Renamed += FileChange;
        _fileWatcher.Deleted += FileChange;

        Zone.Suffix = zoneOptions.Name;

        base.Initialize(zoneOptions);
    }

    /// <summary>Start watching and generating zone files</summary>
    public override void Start(CancellationToken ct)
    {
        ct.Register(Stop);

        // fire first zone generation event on startup
        _timer.Change(TimeSpan.FromSeconds(3), Timeout.InfiniteTimeSpan);
        _fileWatcher.EnableRaisingEvents = true;
    }

    /// <summary>Handler for any file changes</summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void FileChange(object sender, FileSystemEventArgs e) => _timer.Change(FileSettlementPeriod, Timeout.InfiniteTimeSpan);

    /// <summary>Stop watching</summary>
    private void Stop() => _fileWatcher.EnableRaisingEvents = false;

    /// <summary>Handler for settlement completion</summary>
    /// <param name="state"></param>
    private void OnTimer(object state)
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        Task.Run(GenerateZone).ContinueWith(t => Notify(t.Result));
    }


    public override void Dispose()
    {
        if (_fileWatcher != null)
        {
            _fileWatcher.EnableRaisingEvents = false;
            _fileWatcher.Dispose();
        }

        if (_timer == null) return;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _timer.Dispose();
    }
}