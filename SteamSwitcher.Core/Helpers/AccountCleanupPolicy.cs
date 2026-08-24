using SteamSwitcher.Core.Models;

namespace SteamSwitcher.Core.Helpers;

public static class AccountCleanupPolicy
{
    public static IReadOnlyList<SteamAccount> GetCandidates(
        IReadOnlyList<SteamAccount> accounts,
        int months,
        DateTimeOffset? now = null)
    {
        if (months is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(months));

        var cutoff = (now ?? DateTimeOffset.UtcNow)
            .AddMonths(-months)
            .ToUnixTimeSeconds();

        return accounts
            .Where(account => !account.IsActive)
            .Where(account => account.Timestamp > 0 && account.Timestamp < cutoff)
            .OrderBy(account => account.Timestamp)
            .ToList();
    }
}
