using SteamSwitcher.Core.Models;

namespace SteamSwitcher.Core.Services;

public interface IPlaytimeBaselineService
{
    Task<int> GetBaselineAsync(string steamId64, string appId);
    Task SetBaselineAsync(string steamId64, string appId, int playtimeMinutes);
    int CalculateDelta(int currentMinutes, int baselineMinutes);
    Task<DateTime?> GetBaselineDateAsync(string steamId64, string appId);
}