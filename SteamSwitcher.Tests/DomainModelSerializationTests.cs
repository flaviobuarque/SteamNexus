using System.Text.Json;
using FluentAssertions;
using SteamSwitcher.Core.Models;
using Xunit;

namespace SteamSwitcher.Tests;

public sealed class DomainModelSerializationTests
{
    [Fact]
    public void SteamGame_Serialization_IgnoresCircularOwnerAccount()
    {
        var account = new SteamAccount
        {
            SteamId64 = "76561198000000001",
            AccountName = "test_user",
            PersonaName = "Player"
        };

        var game = new SteamGame
        {
            AppId = "440",
            Name = "Team Fortress 2",
            OwnerAccount = account
        };

        var json = JsonSerializer.Serialize(game);

        json.Should().NotContain("\"OwnerAccount\":");
        json.Should().NotContain("\"PlaytimeFormatted\":");
        json.Should().NotContain("\"SizeFormatted\":");
        json.Should().NotContain("\"DriveLetter\":");
        json.Should().NotContain("\"SizeAndDrive\":");
    }

    [Fact]
    public void SteamAccount_Serialization_IgnoresPresentationProperties()
    {
        var account = new SteamAccount
        {
            SteamId64 = "76561198000000001",
            AccountName = "test_user",
            PersonaName = "Player"
        };

        var json = JsonSerializer.Serialize(account);

        json.Should().NotContain("\"DisplayName\":");
        json.Should().NotContain("\"AvatarSource\":");
        json.Should().NotContain("\"SteamId32\":");
    }
}
