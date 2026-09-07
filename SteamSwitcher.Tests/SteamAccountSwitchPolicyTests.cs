using System.Text;
using FluentAssertions;
using SteamSwitcher.Core.Services;
using Xunit;

namespace SteamSwitcher.Tests;

public sealed class SteamAccountSwitchPolicyTests
{
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
