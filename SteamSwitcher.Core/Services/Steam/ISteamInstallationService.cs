using SteamSwitcher.Core.Models;

namespace SteamSwitcher.Core.Services;

public interface ISteamInstallationService
{
    IReadOnlyList<SteamInstallation> Installations { get; }
    SteamInstallation? SelectedInstallation { get; }
    event EventHandler? SelectedInstallationChanged;

    Task DiscoverAsync(CancellationToken ct = default);
    Task SelectAsync(string installationId, CancellationToken ct = default);
    Task AddCustomPathAsync(string path, CancellationToken ct = default);
    Task RenameAsync(string installationId, string? displayName, CancellationToken ct = default);
    Task RemoveCustomPathAsync(string installationId, CancellationToken ct = default);
    SteamOperationContext CaptureContext();
}
