using FluentAssertions;
using SteamSwitcher.Core.Helpers;
using SteamSwitcher.Core.Models;
using Xunit;

namespace SteamSwitcher.Tests;

public sealed class AccountCleanupPolicyTests
{
    [Fact]
    public void GetCandidates_ProtectsActiveRecentAndUnknownAccounts()
    {
        var now = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        var accounts = new List<SteamAccount>
        {
            Create("old", now.AddMonths(-7), isActive: false),
            Create("active-old", now.AddYears(-2), isActive: true),
            Create("recent", now.AddDays(-10), isActive: false),
            Create("unknown", null, isActive: false)
        };

        var candidates = AccountCleanupPolicy.GetCandidates(accounts, 6, now);

        candidates.Should().ContainSingle(account => account.AccountName == "old");
        candidates.Should().NotContain(account => account.IsActive);
        candidates.Should().NotContain(account => account.Timestamp == 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void GetCandidates_RejectsPeriodsOutsideDialogRange(int months)
    {
        var action = () => AccountCleanupPolicy.GetCandidates([], months);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static SteamAccount Create(
        string accountName,
        DateTimeOffset? lastAccess,
        bool isActive) => new()
    {
        SteamId64 = accountName,
        AccountName = accountName,
        PersonaName = accountName,
        Timestamp = lastAccess?.ToUnixTimeSeconds() ?? 0,
        IsActive = isActive
    };
}
