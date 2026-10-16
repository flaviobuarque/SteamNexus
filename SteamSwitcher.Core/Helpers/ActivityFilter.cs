using SteamSwitcher.Core.Services;

namespace SteamSwitcher.Core.Helpers;

public static class ActivityFilter
{
    public static ActivityEntry[] Apply(IEnumerable<ActivityEntry> entries, DateTime? from, DateTime? until, int category, string? search = null)
    {
        var query = entries.Where(e => (!from.HasValue || e.Timestamp.LocalDateTime.Date >= from.Value.Date)
            && (!until.HasValue || e.Timestamp.LocalDateTime.Date <= until.Value.Date)
            && (category == 0 || (category == 6 ? e.NeedsAttention : Category(e.Kind) == category)));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(e =>
                (e.Title != null && e.Title.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (e.Detail != null && e.Detail.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (e.SourceAccount != null && e.SourceAccount.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (e.TargetAccount != null && e.TargetAccount.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (e.Subject != null && e.Subject.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (e.Operation != null && e.Operation.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (e.Accounts != null && e.Accounts.Any(a => a.Contains(term, StringComparison.OrdinalIgnoreCase))));
        }

        return query.OrderByDescending(e => e.Timestamp).ToArray();
    }

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
