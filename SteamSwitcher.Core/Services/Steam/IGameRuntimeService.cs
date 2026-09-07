using SteamSwitcher.Core.Models;

namespace SteamSwitcher.Core.Services;

public sealed record GameProcessIdentity(int Id, DateTime StartedUtc, string Executable);
public sealed record GameRuntimeSnapshot(bool IsRunning, bool IsUncertain, IReadOnlyList<GameProcessIdentity> Processes);

public interface IGameRuntimeService
{
    Task<IReadOnlyDictionary<string, GameRuntimeSnapshot>> InspectAsync(IReadOnlyList<SteamGame> games, CancellationToken ct = default);
    Task<bool> StopAsync(SteamGame game, GameRuntimeSnapshot snapshot, bool force, CancellationToken ct = default);
    void BringToFront(GameRuntimeSnapshot snapshot);
    void ValidateUninstallTarget(SteamGame game);
    void RequestUninstall(SteamGame game);
}
