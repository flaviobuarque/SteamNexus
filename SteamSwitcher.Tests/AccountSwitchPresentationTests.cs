using FluentAssertions;
using SteamSwitcher.Core.Models;
using SteamSwitcher.Helpers;
using Xunit;

namespace SteamSwitcher.Tests;

public sealed class AccountSwitchPresentationTests
{
    private static SteamAccount Account(string id, string installation = "steam") => new()
        { SteamId64 = id, AccountName = id, InstallationId = installation };

    [Fact]
    public void SelectedAccountSurvivesSteamRestartUntilConfirmed()
    {
        var state = new AccountSwitchPresentation();
        var previous = Account("1");
        var target = Account("2");
        state.Begin(target);
        state.Resolve(null).Should().BeSameAs(target);
        state.Resolve(previous).Should().BeSameAs(target);
        state.Complete();
        state.Resolve(null).Should().BeSameAs(target);
        state.Resolve(previous).Should().BeSameAs(target);
        state.Resolve(Account("2", "other-installation")).Should().BeSameAs(target);
        state.Resolve(target).Should().BeSameAs(target);
        state.Resolve(null).Should().BeNull(); // real logout after confirmation
    }

    [Fact]
    public void FailureClearsPendingSelectionAndInvalidatesOlderPolls()
    {
        var state = new AccountSwitchPresentation();
        var revision = state.Revision;
        state.Begin(Account("2"));
        state.Revision.Should().BeGreaterThan(revision);
        state.Cancel();
        state.Resolve(null).Should().BeNull();
    }

    [Fact]
    public void FailedSteamStartupDoesNotLeaveAFictitiousSessionForever()
    {
        var clock = new TestClock();
        var state = new AccountSwitchPresentation(clock);
        state.Begin(Account("2"));
        state.Complete();
        clock.Now = clock.Now.AddMinutes(3);
        state.Resolve(null).Should().BeNull();
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
