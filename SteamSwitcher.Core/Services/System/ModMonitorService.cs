using Microsoft.Extensions.Logging;

namespace SteamSwitcher.Core.Services;

public class ModMonitorService(ILogger<ModMonitorService> logger) : IModMonitorService
{
    private readonly List<DetectedMod> _mods = [];
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly object _modsLock = new();

    public IReadOnlyList<DetectedMod> DetectedMods
    {
        get
        {
            lock (_modsLock)
                return _mods.ToList();
        }
    }

    public event EventHandler<DetectedMod>? ModDetected;
    public event EventHandler<DetectedMod>? ModRemoved;

    private static readonly string[] _watchedFolders =
    [
        "plugins", "millennium", "skins"
    ];

    public async Task ScanAsync(string steamPath, CancellationToken ct = default)
    {
        var found = new List<DetectedMod>();

        foreach (var folder in _watchedFolders)
        {
            var fullPath = Path.Combine(steamPath, folder);
            if (!Directory.Exists(fullPath)) continue;

            await ScanFolderAsync(fullPath, folder, found, ct);
        }

        lock (_modsLock)
        {
            _mods.Clear();
            _mods.AddRange(found);
        }

        logger.LogInformation("{Count} mods detectados", found.Count);
    }

    public void StartWatching(string steamPath)
    {
        StopWatching();

        foreach (var folder in _watchedFolders)
        {
            var fullPath = Path.Combine(steamPath, folder);
            if (!Directory.Exists(fullPath)) continue;

            var watcher = new FileSystemWatcher(fullPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            watcher.Created += (_, e) =>
            {
                var mod = CreateMod(e.FullPath, folder);
                lock (_modsLock)
                    _mods.Add(mod);
                ModDetected?.Invoke(this, mod);
            };

            watcher.Deleted += (_, e) =>
            {
                DetectedMod? mod;
                lock (_modsLock)
                {
                    mod = _mods.FirstOrDefault(m => m.Path == e.FullPath);
                    if (mod is not null)
                        _mods.Remove(mod);
                }
                if (mod is not null)
                    ModRemoved?.Invoke(this, mod);
            };

            _watchers.Add(watcher);
        }
    }

    public void StopWatching()
    {
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
    }

    private static async Task ScanFolderAsync(
        string path, string folderName, List<DetectedMod> target, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                ct.ThrowIfCancellationRequested();
                target.Add(CreateMod(entry, folderName));
            }
        }, ct);
    }

    private static DetectedMod CreateMod(string fullPath, string folderName)
    {
        var name = Path.GetFileName(fullPath);
        var type = folderName switch
        {
            "plugins" => ModType.Plugin,
            "millennium" => ModType.Millennium,
            "skins" => ModType.Skin,
            _ => ModType.Unknown
        };

        var suspicious = type != ModType.Plugin &&
            (fullPath.EndsWith(".exe") || fullPath.EndsWith(".dll"));

        return new DetectedMod
        {
            Name = name,
            Type = type,
            Path = fullPath,
            IsSuspicious = suspicious
        };
    }
}
