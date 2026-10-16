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

    [Fact]
    public void Filter_WithSearchQuery_MatchesAccountAndSubject()
    {
        var now = DateTimeOffset.Now;
        var entries = new[]
        {
            new ActivityEntry(now, ActivityKind.AccountSwitched, SourceAccount: "alpha_user", TargetAccount: "beta_user"),
            new ActivityEntry(now.AddMinutes(-5), ActivityKind.GameStarted, Subject: "Cyberpunk 2077"),
            new ActivityEntry(now.AddMinutes(-10), ActivityKind.PreferenceRepaired, Operation: "RememberPassword")
        };

        ActivityFilter.Apply(entries, null, null, 0, "beta").Should().ContainSingle()
            .Which.TargetAccount.Should().Be("beta_user");

        ActivityFilter.Apply(entries, null, null, 0, "cyber").Should().ContainSingle()
            .Which.Subject.Should().Be("Cyberpunk 2077");

        ActivityFilter.Apply(entries, null, null, 0, "password").Should().ContainSingle()
            .Which.Operation.Should().Be("RememberPassword");

        ActivityFilter.Apply(entries, null, null, 0, "nonexistent").Should().BeEmpty();
    }

    [Fact]
    public void Filter_WithCategory6_ReturnsOnlyAttentionItems()
    {
        var now = DateTimeOffset.Now;
        var entries = new[]
        {
            new ActivityEntry(now, ActivityKind.AccountSwitched),
            new ActivityEntry(now.AddMinutes(-1), ActivityKind.AccountSwitchFailed),
            new ActivityEntry(now.AddMinutes(-2), ActivityKind.GameStarted),
            new ActivityEntry(now.AddMinutes(-3), ActivityKind.GameLaunchFailed)
        };

        var attentionItems = ActivityFilter.Apply(entries, null, null, 6);
        attentionItems.Should().HaveCount(2);
        attentionItems.Should().OnlyContain(e => e.NeedsAttention);
    }

    [Fact]
    public void ActivityEntry_ComputesHelperPropertiesCorrectly()
    {
        var now = DateTimeOffset.Now;
        var switchEntry = new ActivityEntry(now, ActivityKind.AccountSwitched, "user1", "user2");
        switchEntry.CategoryName.Should().Be("Troca de Conta");
        switchEntry.StatusName.Should().Be("Sucesso");
        switchEntry.HasTransition.Should().BeTrue();
        switchEntry.TransitionSummary.Should().Be("user1  ➔  user2");
        switchEntry.HasSubtitle.Should().BeTrue();
        switchEntry.Subtitle.Should().Be("user1  ➔  user2");

        var failedGame = new ActivityEntry(now.AddMinutes(-15), ActivityKind.GameLaunchFailed, Subject: "Elden Ring");
        failedGame.CategoryName.Should().Be("Jogo");
        failedGame.StatusName.Should().Be("Falha");
        failedGame.HasSubtitle.Should().BeTrue();
        failedGame.Subtitle.Should().Be("🎮 Elden Ring");
        failedGame.RelativeTime.Should().Be("15 min atrás");
        failedGame.IsGame.Should().BeTrue();
        failedGame.IsAccountSwitch.Should().BeFalse();
        failedGame.FailureReason.Should().NotBeNullOrWhiteSpace();

        switchEntry.IsAccountSwitch.Should().BeTrue();
        switchEntry.DisplaySourceAccount.Should().Be("user1");
        switchEntry.DisplayTargetAccount.Should().Be("user2");
        switchEntry.FailureReason.Should().BeNull();
    }
}
