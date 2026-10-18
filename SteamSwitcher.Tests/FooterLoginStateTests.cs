using FluentAssertions;
using NSubstitute;
using SteamSwitcher.Core.Models;
using SteamSwitcher.Core.Services;
using SteamSwitcher.Services.Updates;
using SteamSwitcher.ViewModels;
using Wpf.Ui;
using Xunit;

namespace SteamSwitcher.Tests;

public sealed class FooterLoginStateTests
{
    [Fact]
    public void TrayDistinguishesClosedSteamFromRunningWithoutAnAccount()
    {
        var vm = new MainViewModel(Substitute.For<ISteamAccountService>(),
            Substitute.For<ISteamLocatorService>(), Substitute.For<ISnackbarService>(),
            Substitute.For<IAppSettingsService>(), Substitute.For<IUpdateService>(),
            Substitute.For<ISteamGameService>(), Substitute.For<IGameRuntimeService>());
        vm.TrayAccountTitle.Should().Be("Steam não está em execução");
        vm.TrayAccountStatusText.Should().NotContain("Conta ativa");
        vm.IsSteamRunning = true;
        vm.TrayAccountTitle.Should().Be("Steam em execução");
        vm.TrayAccountStatusText.Should().Be("Nenhuma conta conectada");
        vm.ApplyActiveAccount(new SteamAccount { SteamId64 = "1", AccountName = "test", WantsOfflineMode = true });
        vm.TrayAccountStatusText.Should().Be("Conta ativa · Offline");
        vm.IsSwitchingAccount = true;
        vm.TrayAccountStatusText.Should().Be("Trocando conta...");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(LoginState.Online)]
    public async Task ClickFromOnlineSavesOfflineAndSessionUpdatesCannotOverwriteIt(LoginState? initial)
    {
        var settings = Substitute.For<IAppSettingsService>();
        var stored = new AppSettings { DefaultLoginStateOverride = initial };
        settings.Current.Returns(stored);
        var vm = new MainViewModel(Substitute.For<ISteamAccountService>(),
            Substitute.For<ISteamLocatorService>(), Substitute.For<ISnackbarService>(),
            settings, Substitute.For<IUpdateService>(),
            Substitute.For<ISteamGameService>(), Substitute.For<IGameRuntimeService>());
        var notifications = new List<string?>();
        vm.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);

        vm.DefaultLoginStateText.Should().Be("Online");
        await vm.ToggleLoginStateCommand.ExecuteAsync(null);
        stored.DefaultLoginStateOverride.Should().Be(LoginState.Offline);
        notifications.Should().Contain(nameof(vm.DefaultLoginStateText));
        vm.ApplyActiveAccount(new SteamAccount { SteamId64 = "1", AccountName = "test", WantsOfflineMode = false });
        vm.StatusLoginState.Should().Be("Online");
        vm.DefaultLoginStateText.Should().Be("Offline");
        vm.ApplyActiveAccount(null);
        vm.DefaultLoginStateText.Should().Be("Offline");
        await vm.ToggleLoginStateCommand.ExecuteAsync(null);
        stored.DefaultLoginStateOverride.Should().Be(LoginState.Online);
        vm.DefaultLoginStateText.Should().Be("Online");
        await settings.Received(2).SaveAsync(stored);
    }

    [Fact]
    public void ApplyActiveAccount_WithOfflineOverride_SetsStatusLoginStateToOffline()
    {
        var settings = Substitute.For<IAppSettingsService>();
        settings.Current.Returns(new AppSettings());
        var vm = new MainViewModel(Substitute.For<ISteamAccountService>(),
            Substitute.For<ISteamLocatorService>(), Substitute.For<ISnackbarService>(),
            settings, Substitute.For<IUpdateService>(),
            Substitute.For<ISteamGameService>(), Substitute.For<IGameRuntimeService>());

        vm.ApplyActiveAccount(new SteamAccount
        {
            SteamId64 = "1",
            AccountName = "test",
            WantsOfflineMode = false,
            LoginStateOverride = LoginState.Offline
        });

        vm.StatusLoginState.Should().Be("Offline");
        vm.TrayAccountStatusText.Should().Be("Conta ativa · Offline");
    }
}
