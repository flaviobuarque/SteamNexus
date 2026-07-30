using SteamSwitcher.Core.Models;

namespace SteamSwitcher.Core.Services;

public interface ISteamGameService
{
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
}