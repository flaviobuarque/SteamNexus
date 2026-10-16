using SteamSwitcher.Core.Services;

namespace SteamSwitcher.Core.Helpers;

public static class ActivityFilter
{
    public static ActivityEntry[] Apply(IEnumerable<ActivityEntry> entries, DateTime? from, DateTime? until, int category)
        => entries.Where(e => (!from.HasValue || e.Timestamp.LocalDateTime.Date >= from.Value.Date)
            && (!until.HasValue || e.Timestamp.LocalDateTime.Date <= until.Value.Date)
            && (category == 0 || Category(e.Kind) == category))
            .OrderByDescending(e => e.Timestamp).ToArray();

    private static int Category(ActivityKind kind) => kind switch
    {
        ActivityKind.AccountSwitched or ActivityKind.AccountSwitchFailed => 1,
        ActivityKind.GameStarted or ActivityKind.GameLaunchFailed => 2,
        ActivityKind.PreferenceRepaired or ActivityKind.RepairFailed => 3,
        ActivityKind.UpdateReady or ActivityKind.UpdateFailed => 4,
        ActivityKind.SettingsRecovered => 5,
        _ => 0
    };
}
