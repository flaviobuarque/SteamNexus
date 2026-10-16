using FluentAssertions;
using SteamSwitcher.Core.Models;
using SteamSwitcher.Core.Services;
using Xunit;

namespace SteamSwitcher.Tests;

public sealed class GameRuntimeServiceTests
{
    [Fact]
    public async Task InspectAsync_ReturnsEmptyDictionary_WhenNoGamesProvided()
    {
        var service = new GameRuntimeService();
        var result = await service.InspectAsync([]);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task InspectAsync_RunsCleanly_WithoutThrowingOnSystemProcesses()
    {
        var service = new GameRuntimeService();
        var dummyGame = new SteamGame
        {
            AppId = "440",
            Name = "Team Fortress 2",
            InstallDir = "Team Fortress 2",
            LibraryPath = @"C:\SteamLibrary"
        };

        var result = await service.InspectAsync([dummyGame]);

        result.Should().ContainKey(dummyGame.UniqueKey);
        result[dummyGame.UniqueKey].Should().NotBeNull();
    }
}
