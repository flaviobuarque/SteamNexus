using System.Text;
using FluentAssertions;
using SteamSwitcher.Core.Services;
using Xunit;

namespace SteamSwitcher.Tests;

public sealed class SteamSessionResolverTests
{
    [Theory]
    [InlineData("1", "1", "1", null, true)]
    [InlineData("1", "1", "1", "0", true)]
    [InlineData("0", "1", "1", null, false)]
    [InlineData("1", "0", "1", null, false)]
    [InlineData("1", "1", "0", null, false)]
    [InlineData("1", "1", "1", "999", false)]
    [InlineData("0", "1", "1", "1", true)]
    public void OfflineFallbackRequiresRememberedSelectedAccount(
        string offline, string remembered, string autoLogin, string? activeUser, bool expected)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes($$"""
            "users" {
                "76561197960265729" {
                    "AccountName" "test"
                    "AutoLogin" "{{autoLogin}}"
                    "RememberPassword" "{{remembered}}"
                    "WantsOfflineMode" "{{offline}}"
                }
            }
            """));
        var snapshot = SteamAccountSnapshotParser.Parse(input);
        (SteamSessionResolver.Resolve(snapshot, activeUser) is not null).Should().Be(expected);
    }
}
