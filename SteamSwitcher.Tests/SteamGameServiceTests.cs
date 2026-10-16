using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SteamSwitcher.Core.Models;
using SteamSwitcher.Core.Services;
using Xunit;

namespace SteamSwitcher.Tests;

public sealed class SteamGameServiceTests
{
    [Fact]
    public async Task GetInstalledGamesAsync_CachesManifestMetadata_WhenManifestUnchanged()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"steam-game-test-{Guid.NewGuid():N}");
        var steamApps = Path.Combine(tempDir, "steamapps");
        Directory.CreateDirectory(steamApps);

        var manifestPath = Path.Combine(steamApps, "appmanifest_440.acf");
        var acfContent = "\"AppState\"\n{\n\t\"appid\"\t\t\"440\"\n\t\"name\"\t\t\"Team Fortress 2\"\n\t\"installdir\"\t\t\"Team Fortress 2\"\n\t\"LastOwner\"\t\t\"76561198000000000\"\n\t\"SizeOnDisk\"\t\t\"15000000000\"\n}\n";
        await File.WriteAllTextAsync(manifestPath, acfContent);

        try
        {
            var locator = Substitute.For<ISteamLocatorService>();
            var installationService = Substitute.For<ISteamInstallationService>();
            var accountService = Substitute.For<ISteamAccountService>();
            var settingsService = Substitute.For<IAppSettingsService>();
            var logger = Substitute.For<ILogger<SteamGameService>>();

            var installation = new SteamInstallation
            {
                Id = "main",
                DisplayName = "Main Steam",
                RootPath = tempDir,
                SteamExePath = Path.Combine(tempDir, "steam.exe"),
                LoginUsersPath = Path.Combine(tempDir, "config", "loginusers.vdf"),
                IsValid = true
            };
            installationService.Installations.Returns([installation]);

            var service = new SteamGameService(
                locator,
                installationService,
                accountService,
                settingsService,
                logger);

            var accounts = new List<SteamAccount>
            {
                new() { SteamId64 = "76561198000000000", AccountName = "user1", InstallationId = "main" }
            };

            // First call reads and caches
            var games1 = await service.GetInstalledGamesAsync(accounts);
            games1.Should().HaveCount(1);
            games1[0].AppId.Should().Be("440");
            games1[0].Name.Should().Be("Team Fortress 2");
            games1[0].OwnerSteamId64.Should().Be("76561198000000000");

            // Second call should return from cache cleanly
            var games2 = await service.GetInstalledGamesAsync(accounts);
            games2.Should().HaveCount(1);
            games2[0].AppId.Should().Be("440");
            games2[0].Name.Should().Be("Team Fortress 2");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
