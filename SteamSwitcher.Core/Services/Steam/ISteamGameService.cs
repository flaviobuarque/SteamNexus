using SteamSwitcher.Core.Models;

namespace SteamSwitcher.Core.Services;

public interface ISteamGameService
{
    IReadOnlyList<string> GetLibraryManifestDirectories();

    Task<IReadOnlyList<SteamGame>> GetInstalledGamesAsync(
        IReadOnlyList<SteamAccount> accounts,
        CancellationToken ct = default);

    Task LaunchGameAsync(
        SteamGame game,
        SteamAccount account,
        CancellationToken ct = default);

    Task LoadPlaytimeAsync(
        SteamGame game,
        string steamPath,
        CancellationToken ct = default);

    Task<Dictionary<string, int>> LoadGameLoginStatesAsync();

    Task SetGameLoginStateAsync(string appId, LoginState? state, CancellationToken ct = default);

    Task<Dictionary<string, string>> LoadManualCoversAsync();

    Task SetManualCoverAsync(string appId, string? path, CancellationToken ct = default);

    Task ClearManualCoverAsync(string appId, CancellationToken ct = default);
}
