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
        var action = () => service.CaptureContext();
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task InstallationWithoutLoginUsers_RemainsValidForRecovery()
    {
        var root = Path.Combine(_root, "Recoverable");
        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.WriteAllBytes(Path.Combine(root, "Steam.exe"), [0x4D, 0x5A]);
        var settings = new AppSettings
        {
            SteamInstallPath = root,
            KnownSteamInstallPaths = [root],
        };
        var (service, _, _) = CreateService(settings);

        await service.DiscoverAsync();

        service.SelectedInstallation.Should().NotBeNull();
        service.SelectedInstallation!.IsValid.Should().BeTrue();
        service.SelectedInstallation.HasLoginUsersFile.Should().BeFalse();
        service.SelectedInstallation.StatusText.Should().Contain("recuperação disponível");
        service.CaptureContext().LoginUsersPath.Should().EndWith("loginusers.vdf");
    }

    [Fact]
    public async Task Discover_DifferentiatesSameFolderNameAndPersistsCustomName()
    {
        var first = CreateSteam(Path.Combine("Primary", "Steam"), "76561198000000005");
        var second = CreateSteam(Path.Combine("Secondary", "Steam"), "76561198000000006");
        var settings = new AppSettings { KnownSteamInstallPaths = [first, second] };
        var (service, _, settingsService) = CreateService(settings);

        await service.DiscoverAsync();

        service.Installations.Select(i => i.DisplayName).Should().OnlyHaveUniqueItems();
        var secondInstallation = service.Installations.Single(i => i.RootPath == second);
        await service.RenameAsync(secondInstallation.Id, "Steam secundária");

        service.Installations.Single(i => i.RootPath == second).DisplayName
            .Should().Be("Steam secundária");
        settings.SteamInstallationNames[second].Should().Be("Steam secundária");
        await settingsService.Received().SaveAsync(settings);
    }

    [Fact]
    public async Task CaptureContext_ByInstallationId_DoesNotDependOnCurrentSelection()
    {
        var first = CreateSteam("ContextFirst", "76561198000000007");
        var second = CreateSteam("ContextSecond", "76561198000000008");
        var settings = new AppSettings
        {
            SteamInstallPath = first,
            KnownSteamInstallPaths = [first, second],
        };
        var (service, _, _) = CreateService(settings);
        await service.DiscoverAsync();
        var secondInstallation = service.Installations.Single(i => i.RootPath == second);

        var context = service.CaptureContext(secondInstallation.Id);

        context.RootPath.Should().Be(second);
        service.SelectedInstallation!.RootPath.Should().Be(first);
    }

    [Fact]
    public async Task GetAllAccounts_KeepsDuplicateSteamIdSeparatedByInstallation()
    {
        const string sharedSteamId = "76561198000000009";
        var first = CreateSteam("UnifiedFirst", sharedSteamId);
        var second = CreateSteam("UnifiedSecond", sharedSteamId);
        var installations = new[]
        {
            CreateInstallation("first", first),
            CreateInstallation("second", second),
        };
        var installationService = Substitute.For<ISteamInstallationService>();
        installationService.Installations.Returns(installations);
        installationService.SelectedInstallation.Returns(installations[0]);
        var locator = Substitute.For<ISteamLocatorService>();
        var settingsService = Substitute.For<IAppSettingsService>();
        settingsService.Current.Returns(new AppSettings());
        var accountService = new SteamAccountService(
            locator,
            installationService,
            settingsService,
            new SteamKnownAccountStore(Path.Combine(
                Path.GetTempPath(), $"steam-known-{Guid.NewGuid():N}.json")),
            NullLogger<SteamAccountService>.Instance);

        var accounts = await accountService.GetAllAccountsAsync();

        accounts.Should().HaveCount(2);
        accounts.Select(account => account.UniqueKey).Should().OnlyHaveUniqueItems();
        accounts.Select(account => account.InstallationId)
            .Should().BeEquivalentTo("first", "second");
    }

    [Fact]
    public async Task GetAllAccounts_RestoresStoredAccountOnlyToItsInstallation()
    {
        var first = CreateSteam("StoredFirst", "76561198000000012");
        var second = CreateSteam("StoredSecond", "76561198000000013");
        var installations = new[]
        {
            CreateInstallation("first", first),
            CreateInstallation("second", second),
        };
        var installationService = Substitute.For<ISteamInstallationService>();
        installationService.Installations.Returns(installations);
        installationService.SelectedInstallation.Returns(installations[0]);
        var locator = Substitute.For<ISteamLocatorService>();
        var settingsService = Substitute.For<IAppSettingsService>();
        settingsService.Current.Returns(new AppSettings());
        var storePath = Path.Combine(
            Path.GetTempPath(), $"steam-known-{Guid.NewGuid():N}.json");

        try
        {
            var store = new SteamKnownAccountStore(storePath);
            await store.RememberAsync(
            [
                new SteamAccount
                {
                    InstallationId = "second",
                    SteamId64 = "76561198000000014",
                    AccountName = "recovered_login",
                    PersonaName = "Recovered",
                },
            ]);
            var accountService = new SteamAccountService(
                locator,
                installationService,
                settingsService,
                store,
                NullLogger<SteamAccountService>.Instance);

            var accounts = await accountService.GetAllAccountsAsync();

            var recovered = accounts.Single(a => a.SteamId64 == "76561198000000014");
            recovered.InstallationId.Should().Be("second");
            recovered.InstallationRootPath.Should().Be(second);
            recovered.RememberPassword.Should().BeFalse();
            recovered.IsActive.Should().BeFalse();
        }
        finally
        {
            File.Delete(storePath);
        }
    }

    [Fact]
    public async Task GetAllAccounts_ShowsArchivedAccountsWhenLoginUsersIsMissing()
    {
        var root = Path.Combine(_root, "MissingVdf");
        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.WriteAllBytes(Path.Combine(root, "Steam.exe"), [0x4D, 0x5A]);
        var installation = CreateInstallation("recoverable", root) with
        {
            HasLoginUsersFile = false,
        };
        var installationService = Substitute.For<ISteamInstallationService>();
        installationService.Installations.Returns([installation]);
        installationService.SelectedInstallation.Returns(installation);
        var settingsService = Substitute.For<IAppSettingsService>();
        settingsService.Current.Returns(new AppSettings());
        var storePath = Path.Combine(Path.GetTempPath(), $"steam-known-{Guid.NewGuid():N}.json");

        try
        {
            var store = new SteamKnownAccountStore(storePath);
            await store.RememberAsync(
            [
                new SteamAccount
                {
                    InstallationId = "recoverable",
                    SteamId64 = "76561198000000015",
                    AccountName = "archived_login",
                    PersonaName = "Archived",
                },
            ]);
            var service = new SteamAccountService(
                Substitute.For<ISteamLocatorService>(),
                installationService,
                settingsService,
                store,
                NullLogger<SteamAccountService>.Instance);

            var account = (await service.GetAllAccountsAsync()).Should().ContainSingle().Subject;

            account.IsArchived.Should().BeTrue();
            account.InstallationId.Should().Be("recoverable");
            account.AccountName.Should().Be("archived_login");
        }
        finally
        {
            File.Delete(storePath);
        }
    }

    [Fact]
    public async Task Relocate_PreservesCustomNameAndDefaultSelection()
    {
        var original = CreateSteam("RelocateOld", "76561198000000010");
        var replacement = CreateSteam("RelocateNew", "76561198000000011");
        var settings = new AppSettings
        {
            SteamInstallPath = original,
            KnownSteamInstallPaths = [original],
            SteamInstallationNames = new(StringComparer.OrdinalIgnoreCase)
            {
                [original] = "Steam portátil",
            },
        };
        var (service, _, _) = CreateService(settings);
        await service.DiscoverAsync();

        await service.RelocateAsync(
            service.SelectedInstallation!.Id,
            Path.Combine(replacement, "Steam.exe"));

        service.SelectedInstallation!.RootPath.Should().Be(replacement);
        service.SelectedInstallation.DisplayName.Should().Be("Steam portátil");
        service.SelectedInstallation.IsSelected.Should().BeTrue();
        settings.KnownSteamInstallPaths.Should().Contain(replacement).And.NotContain(original);
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

    private static SteamInstallation CreateInstallation(string id, string root) => new()
    {
        Id = id,
        RootPath = root,
        SteamExePath = Path.Combine(root, "Steam.exe"),
        LoginUsersPath = Path.Combine(root, "config", "loginusers.vdf"),
        DisplayName = id,
        IsValid = true,
    };

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
