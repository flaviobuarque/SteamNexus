using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SteamSwitcher.Core.Models;
using SteamSwitcher.Core.Services;
using Xunit;

namespace SteamSwitcher.Tests;

public sealed class SteamInstallationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "SteamNexusTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AddCustomPath_SelectsAndPersistsInstallation()
    {
        var steamRoot = CreateSteam("PortableA", "76561198000000001");
        var (service, settings, settingsService) = CreateService();

        await service.AddCustomPathAsync(Path.Combine(steamRoot, "Steam.exe"));

        service.SelectedInstallation.Should().NotBeNull();
        service.SelectedInstallation!.RootPath.Should().Be(steamRoot);
        service.SelectedInstallation.IsCustom.Should().BeTrue();
        service.SelectedInstallation.AccountCount.Should().Be(1);
        settings.SteamInstallPath.Should().Be(steamRoot);
        settings.KnownSteamInstallPaths.Should().ContainSingle(steamRoot);
        await settingsService.Received().SaveAsync(settings);
    }

    [Fact]
    public async Task Discover_DeduplicatesEquivalentPaths()
    {
        var steamRoot = CreateSteam("PortableB", "76561198000000002");
        var settings = new AppSettings
        {
            KnownSteamInstallPaths = [steamRoot, steamRoot + Path.DirectorySeparatorChar],
        };
        var (service, _, _) = CreateService(settings);

        await service.DiscoverAsync();

        service.Installations.Count(i =>
            string.Equals(i.RootPath, steamRoot, StringComparison.OrdinalIgnoreCase))
            .Should().Be(1);
    }

    [Fact]
    public async Task Select_ChangesTheCapturedOperationContext()
    {
        var first = CreateSteam("First", "76561198000000003");
        var second = CreateSteam("Second", "76561198000000004");
        var settings = new AppSettings { KnownSteamInstallPaths = [first, second] };
        var (service, _, _) = CreateService(settings);
        await service.DiscoverAsync();
        var secondInstallation = service.Installations.Single(i => i.RootPath == second);

        await service.SelectAsync(secondInstallation.Id);
        var context = service.CaptureContext();

        context.RootPath.Should().Be(second);
        context.SteamExePath.Should().Be(Path.Combine(second, "Steam.exe"));
        context.LoginUsersPath.Should().Be(Path.Combine(second, "config", "loginusers.vdf"));
    }

    [Fact]
    public async Task DisconnectedCustomInstallation_RemainsVisibleAndCannotBeCaptured()
    {
        var disconnected = Path.Combine(_root, "Disconnected");
        var settings = new AppSettings
        {
            SteamInstallPath = disconnected,
            KnownSteamInstallPaths = [disconnected],
        };
        var (service, _, _) = CreateService(settings);

        await service.DiscoverAsync();

        service.Installations.Should().Contain(i =>
            i.RootPath == disconnected && !i.IsValid && i.IsCustom);
        var action = service.CaptureContext;
        action.Should().Throw<InvalidOperationException>();
    }

    private (SteamInstallationService Service, AppSettings Settings, IAppSettingsService SettingsService)
        CreateService(AppSettings? settings = null)
    {
        settings ??= new AppSettings();
        var settingsService = Substitute.For<IAppSettingsService>();
        settingsService.Current.Returns(settings);
        settingsService.SaveAsync(Arg.Any<AppSettings>()).Returns(Task.CompletedTask);
        var service = new SteamInstallationService(
            settingsService,
            NullLogger<SteamInstallationService>.Instance);
        return (service, settings, settingsService);
    }

    private string CreateSteam(string name, string steamId64)
    {
        var root = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.WriteAllBytes(Path.Combine(root, "Steam.exe"), [0x4D, 0x5A]);
        File.WriteAllText(Path.Combine(root, "config", "loginusers.vdf"), $$"""
            "users"
            {
                "{{steamId64}}"
                {
                    "AccountName" "test"
                    "PersonaName" "Test"
                    "RememberPassword" "1"
                    "MostRecent" "1"
                    "AutoLogin" "1"
                }
            }
            """);
        return root;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
