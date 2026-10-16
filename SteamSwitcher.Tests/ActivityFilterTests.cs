using FluentAssertions;
using SteamSwitcher.Core.Helpers;
using SteamSwitcher.Core.Services;
using Xunit;

namespace SteamSwitcher.Tests;

public class ActivityFilterTests
{
    [Fact]
    public void Filter_IncludesEntireLastDay_AndBothResultsOfType()
    {
        var day = new DateTime(2026, 9, 20);
        ActivityEntry Entry(DateTime date, ActivityKind kind) => new(new DateTimeOffset(date), kind);
        var entries = new[] {
            Entry(day, ActivityKind.AccountSwitched),
            Entry(day.AddHours(23).AddMinutes(59), ActivityKind.AccountSwitchFailed),
            Entry(day.AddDays(1), ActivityKind.AccountSwitched),
            Entry(day, ActivityKind.GameStarted)
        };
        var filtered = ActivityFilter.Apply(entries, day, day, 1);
        filtered.Should().HaveCount(2);
        filtered[0].Kind.Should().Be(ActivityKind.AccountSwitchFailed);
        ActivityFilter.Apply(entries, null, day, 0).Should().HaveCount(3);
        ActivityFilter.Apply(entries, day.AddDays(1), null, 0).Should().ContainSingle();
        ActivityFilter.Apply(entries, null, null, 0).Should().HaveCount(4);
        ActivityFilter.Apply(entries, null, null, 5).Should().BeEmpty();
    }
}
