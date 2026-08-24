using System.ComponentModel;

namespace SteamSwitcher.Services.Updates;

public interface IUpdateService : INotifyPropertyChanged
{
    string CurrentVersion { get; }
    string AvailableVersion { get; }
    string StatusText { get; }
    string ErrorText { get; }
    string DownloadSpeedText { get; }
    string UpdateActionText { get; }
    int DownloadProgress { get; }
    bool IsConfigured { get; }
    bool IsInstalled { get; }
    bool IsChecking { get; }
    bool IsDownloading { get; }
    bool IsUpdateAvailable { get; }
    bool IsUpdateReady { get; }
    bool CanCheckForUpdates { get; }

    Task CheckForUpdatesAsync(CancellationToken ct = default);
    Task DownloadUpdateAsync(CancellationToken ct = default);
    void ApplyUpdateAndRestart();
}
