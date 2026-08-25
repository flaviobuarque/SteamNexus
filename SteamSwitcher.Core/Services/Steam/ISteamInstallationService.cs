using SteamSwitcher.Core.Models;

namespace SteamSwitcher.Core.Services;

public interface ISteamInstallationService
{
    IReadOnlyList<SteamInstallation> Installations { get; }
    SteamInstallation? SelectedInstallation { get; }
    event EventHandler? SelectedInstallationChanged;

    Task DiscoverAsync(CancellationToken ct = default);
    SteamOperationContext CaptureContext();
}
