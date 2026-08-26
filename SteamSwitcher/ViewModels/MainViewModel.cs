using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SteamSwitcher.Core.Models;
using SteamSwitcher.Core.Services;
using SteamSwitcher.Services.Updates;
using System.Collections.ObjectModel;
using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SteamSwitcher.ViewModels;

public partial class MainViewModel(
    ISteamAccountService accountService,
    ISteamLocatorService locatorService,
    ISnackbarService snackbarService,
    IAppSettingsService settingsService,
    IUpdateService updateService) : ObservableObject
{
    public IUpdateService UpdateService { get; } = updateService;
    [ObservableProperty] private string _activeAccountName = string.Empty;
    [ObservableProperty] private bool _hasActiveAccount;
    [ObservableProperty] private bool _steamNotFound;
    [ObservableProperty] private string _trayTooltip = "Steam Switcher";
    [ObservableProperty] private string _trayActiveAccountText = "Nenhuma conta ativa";
    [ObservableProperty] private ObservableCollection<SteamAccount> _trayAccounts = [];
    [ObservableProperty] private string _activePage = "Accounts";
    [ObservableProperty] private bool _isWindowVisible = true;
    [ObservableProperty] private string _statusAccountName = string.Empty;
    [ObservableProperty] private string _statusLoginState = string.Empty;
    [ObservableProperty] private bool _statusBarVisible = true;
    [ObservableProperty] private string _statusBarLeft = string.Empty;
    [ObservableProperty] private string _statusBarRight = string.Empty;
    [ObservableProperty] private bool _showLoginToggle;
    [ObservableProperty] private string _activeAccountAvatarPath = string.Empty;
    [ObservableProperty] private bool _isSwitchingAccount;

    public bool IsDarkThemeSelected =>
        settingsService.Current.Theme == AppTheme.Dark;
    public bool IsLightThemeSelected =>
        settingsService.Current.Theme == AppTheme.Light;
    public bool IsSystemThemeSelected =>
        settingsService.Current.Theme == AppTheme.System;

    public string TrayAccountStatusText => IsSwitchingAccount ? "Trocando conta..." : "Conta ativa";
    public int AccountGridDensityPercent => NormalizeAccountGridDensity(
        settingsService.Current.AccountGridDensityPercent);
    public string AccountGridDensityText => $"{AccountGridDensityPercent}%";
    public bool CanDecreaseAccountGridDensity => AccountGridDensityPercent > 50;
    public bool CanIncreaseAccountGridDensity => AccountGridDensityPercent < 100;
    public bool ShowAccountGridDensityControls => ActivePage == "Accounts";

    partial void OnActivePageChanged(string value)
        => OnPropertyChanged(nameof(ShowAccountGridDensityControls));

    [RelayCommand]
    private async Task DecreaseAccountGridDensityAsync()
        => await SetAccountGridDensityAsync(AccountGridDensityPercent == 100 ? 75 : 50);

    [RelayCommand]
    private async Task IncreaseAccountGridDensityAsync()
        => await SetAccountGridDensityAsync(AccountGridDensityPercent == 50 ? 75 : 100);

    private async Task SetAccountGridDensityAsync(int percent)
    {
        percent = NormalizeAccountGridDensity(percent);
        if (AccountGridDensityPercent == percent)
            return;

        settingsService.Current.AccountGridDensityPercent = percent;
        await settingsService.SaveAsync(settingsService.Current);

        OnPropertyChanged(nameof(AccountGridDensityPercent));
        OnPropertyChanged(nameof(AccountGridDensityText));
        OnPropertyChanged(nameof(CanDecreaseAccountGridDensity));
        OnPropertyChanged(nameof(CanIncreaseAccountGridDensity));
        WeakReferenceMessenger.Default.Send(new AccountGridDensityChanged());
    }

    private static int NormalizeAccountGridDensity(int percent) => percent switch
    {
        <= 50 => 50,
        <= 75 => 75,
        _ => 100,
    };

    partial void OnIsSwitchingAccountChanged(bool value)
        => OnPropertyChanged(nameof(TrayAccountStatusText));

    public void NotifyAccountSwitchStarted(string targetName, string targetAvatarPath)
    {
        ActiveAccountName = targetName;
        ActiveAccountAvatarPath = targetAvatarPath;
        IsSwitchingAccount = true;
    }

    public void NotifyAccountSwitchFinished()
    {
        IsSwitchingAccount = false;
    }

    public string ToggleWindowText => IsWindowVisible ? "Ocultar" : "Exibir";

    partial void OnIsWindowVisibleChanged(bool value)
        => OnPropertyChanged(nameof(ToggleWindowText));

    public event Action<HotkeyDefinition?>? GlobalHotkeyChanged;

    public void ApplyGlobalHotkey(HotkeyDefinition? hotkey)
    {
        GlobalHotkeyChanged?.Invoke(hotkey);
    }

    public void HideWindowToTray()
    {
        Application.Current.MainWindow?.Hide();
    }

    public void ShowWindowFromTray()
    {
        var window = Application.Current.MainWindow;
        if (window is null)
            return;

        window.Show();
        window.Activate();
    }

    public void UpdateStatusBar(string left, string right = "", bool showLoginToggle = false)
    {
        StatusBarLeft = left;
        StatusBarRight = right;
        ShowLoginToggle = showLoginToggle;
        StatusBarVisible = true;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var steamPath = locatorService.FindSteamInstallPath();
        SteamNotFound = string.IsNullOrEmpty(steamPath);

        // Observa mudancas de conta ativa vindas de outros pontos (ex.: troca
        // manual pelo Steam detectada pelo FileSystemWatcher em AccountsViewModel).
        if (!WeakReferenceMessenger.Default.IsRegistered<ActiveAccountChanged>(this))
        {
            WeakReferenceMessenger.Default.Register<ActiveAccountChanged>(this, async (_, _) =>
            {
                await Application.Current.Dispatcher
                    .InvokeAsync(async () => await RefreshActiveAccountAsync())
                    .Task
                    .Unwrap();
            });
        }

        if (!WeakReferenceMessenger.Default.IsRegistered<SteamInstallationChanged>(this))
        {
            WeakReferenceMessenger.Default.Register<SteamInstallationChanged>(this, async (_, _) =>
            {
                await Application.Current.Dispatcher
                    .InvokeAsync(() => InitializeAsync())
                    .Task
                    .Unwrap();
            });
        }

        try
        {
            var snapshot = await accountService.GetSnapshotAsync(ct);
            TrayAccounts = new ObservableCollection<SteamAccount>(snapshot.Accounts);
            ApplyActiveAccount(snapshot.ActiveAccount
                ?? snapshot.Accounts.FirstOrDefault(a => a.MostRecent));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[MainViewModel.InitializeAsync] falha: {ex}");
        }
    }

    /// <summary>
    /// Re-le do Steam qual conta esta ativa e atualiza todas as UI dependententes
    /// (status bar, tray, avatar, etc.). Idempotente.
    /// </summary>
    public async Task RefreshActiveAccountAsync(CancellationToken ct = default)
    {
        var active = await accountService.GetActiveAccountAsync(ct);

        // Fallback: se o registry/VDF nao retornou nada, pega a conta marcada
        // como MostRecent/IsActive em TrayAccounts (lida do VDF em InitializeAsync).
        // Isto cobre steam recem-fechado, racing com startup do Steam, etc.
        if (active is null)
            active = TrayAccounts.FirstOrDefault(a => a.MostRecent || a.IsActive);

        System.Diagnostics.Debug.WriteLine(
            $"[MainViewModel.RefreshActiveAccountAsync] active={(active is null ? "NULL" : active.AccountName)}");

        ApplyActiveAccount(active);
    }

    /// <summary>
    /// Aplica uma conta ativa ja conhecida em todas as UI dependententes.
    /// Usado tanto em InitializeAsync quanto apos troca iniciada pelo app,
    /// evitando re-ler do registry quando ja sabemos a resposta.
    /// </summary>
    public void ApplyActiveAccount(SteamAccount? active)
    {
        if (active is null)
        {
            HasActiveAccount = false;
            ActiveAccountName = string.Empty;
            StatusAccountName = string.Empty;
            ActiveAccountAvatarPath = string.Empty;
            StatusLoginState = string.Empty;
            TrayTooltip = "Steam Switcher";
            TrayActiveAccountText = "Nenhuma conta ativa";

            foreach (var a in TrayAccounts)
                a.IsActive = false;
            return;
        }

        HasActiveAccount = true;
        ActiveAccountName = active.DisplayName;
        StatusAccountName = active.DisplayName;
        TrayTooltip = $"Steam Switcher - {active.DisplayName}";
        TrayActiveAccountText = $"\u25CF {active.DisplayName}";
        ActiveAccountAvatarPath = string.Empty;

        var appliedState = active.LoginStateOverride
            ?? settingsService.Current.DefaultLoginStateOverride;
        StatusLoginState = appliedState == LoginState.Offline
            ? "Offline"
            : "Online";

        foreach (var a in TrayAccounts)
            a.IsActive = a.SteamId64 == active.SteamId64;
    }

    [RelayCommand]
    private async Task SwitchFromTrayAsync(SteamAccount account)
    {
        snackbarService.Show(
            "Alternando conta",
            $"Entrando como {account.DisplayName}...",
            ControlAppearance.Secondary,
            null,
            TimeSpan.FromSeconds(3));

        try
        {
            await accountService.SwitchAccountAsync(account);
            ApplyActiveAccount(account);

            var settings = settingsService.Current;
            if (settings.AfterAccountSwitch == PostSwitchBehavior.Close)
                Application.Current.Shutdown();
            else if (settings.AfterAccountSwitch == PostSwitchBehavior.MinimizeToTray)
                ToggleWindow();
        }
        catch (Exception ex)
        {
            snackbarService.Show(
                "Erro",
                $"Não foi possível trocar de conta: {ex.Message}",
                ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(5));
        }
    }

    [RelayCommand]
    private void ToggleWindow()
    {
        var window = Application.Current.MainWindow;
        if (window is null)
            return;

        if (window.IsVisible)
            HideWindowToTray();
        else
            ShowWindowFromTray();
    }

    public void NotifyActiveAccountAvatarLoaded(string avatarPath)
    {
        ActiveAccountAvatarPath = avatarPath;
    }

    [RelayCommand]
    private void Exit() => Application.Current.Shutdown();

    [RelayCommand]
    private async Task VerifySteamAsync()
    {
        var steamPath = locatorService.FindSteamInstallPath();
        SteamNotFound = string.IsNullOrEmpty(steamPath);

        if (!SteamNotFound)
            snackbarService.Show(
                "Steam detectado",
                "Instalação encontrada com sucesso.",
                ControlAppearance.Success,
                null,
                TimeSpan.FromSeconds(3));
    }

    [RelayCommand]
    private async Task ApplyThemeAsync(string themeStr)
    {
        var theme = themeStr switch
        {
            "Light" => Core.Models.AppTheme.Light,
            "Dark" => Core.Models.AppTheme.Dark,
            _ => Core.Models.AppTheme.System
        };

        var current = settingsService.Current;
        current.Theme = theme;
        await settingsService.SaveAsync(current);
        App.ApplyTheme(theme);
        RefreshThemeSelection();
    }

    public void RefreshThemeSelection()
    {
        OnPropertyChanged(nameof(IsDarkThemeSelected));
        OnPropertyChanged(nameof(IsLightThemeSelected));
        OnPropertyChanged(nameof(IsSystemThemeSelected));
    }

    [RelayCommand]
    private async Task ToggleLoginStateAsync()
    {
        var current = settingsService.Current;

        var nextState = current.DefaultLoginStateOverride == LoginState.Online
            ? LoginState.Offline
            : LoginState.Online;

        current.DefaultLoginStateOverride = nextState;

        await settingsService.SaveAsync(current);

        StatusLoginState = nextState.ToString();
    }
}
