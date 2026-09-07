using System.Text;
using FluentAssertions;
using SteamSwitcher.Core.Services;
using Xunit;

namespace SteamSwitcher.Tests;

public sealed class SteamAccountSwitchPolicyTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void LaunchUsesRefreshedPreferenceInsteadOfCachedCard(bool cachedValue, bool persistedValue)
    {
        var cached = new SteamSwitcher.Core.Models.SteamAccount
            { SteamId64 = "76561198000000001", AccountName = "test", InstallationId = "steam", RememberPassword = cachedValue };
        var refreshed = new SteamSwitcher.Core.Models.SteamAccount
            { SteamId64 = cached.SteamId64, AccountName = "test", InstallationId = cached.InstallationId, RememberPassword = persistedValue };
        var resolve = () => SteamAccountSwitchPolicy.ResolveRememberedAccount([refreshed], cached.UniqueKey);
        if (persistedValue) resolve().Should().BeSameAs(refreshed);
        else resolve.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData(null, false)]
    [InlineData("2", false)]
    [InlineData("true", false)]
    public void SwitchRequiresExactlyOneInPersistedPasswordField(string? value, bool allowed)
    {
        var field = value is null ? "" : $"\"RememberPassword\" \"{value}\"";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(
            "\"users\" { \"76561198000000001\" { \"AccountName\" \"test\" " + field + " } }"));
        var account = SteamAccountSnapshotParser.Parse(input).Accounts.Single();
        var check = () => SteamAccountSwitchPolicy.RequireRememberedAccount(account);
        if (allowed) check.Should().NotThrow();
        else check.Should().Throw<InvalidOperationException>().WithMessage("*RememberPassword=1*");
        account.RememberPassword.Should().Be(allowed);
    }

    [Fact]
    public void MissingAccountCannotBeRecoveredForAutomaticSwitch()
    {
        var check = () => SteamAccountSwitchPolicy.RequireRememberedAccount(null);
        check.Should().Throw<InvalidOperationException>().WithMessage("*RememberPassword=1*");
    }
}
