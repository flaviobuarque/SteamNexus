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
    IUpdateService updateService,
    ISteamGameService gameService,
    IGameRuntimeService runtimeService) : ObservableObject
{
    [ObservableProperty] private bool _isGameRunning;

    public async Task<bool> CanChangeAccountAsync()
    {
        try
        {
            var accounts = await accountService.GetAllAccountsAsync();
            var games = await gameService.GetInstalledGamesAsync(accounts);
            var states = await runtimeService.InspectAsync(games);
            IsGameRunning = states.Values.Any(state => state.IsRunning || state.IsUncertain);
            if (!IsGameRunning) return true;
            snackbarService.Show("Jogo em execução", "Pare o jogo para trocar de conta.", ControlAppearance.Caution, null, TimeSpan.FromSeconds(4));
        }
        catch
        {
            snackbarService.Show("Não foi possível verificar os jogos", "Tente novamente antes de trocar de conta.", ControlAppearance.Caution, null, TimeSpan.FromSeconds(4));
        }
        return false;
    }
    public IUpdateService UpdateService { get; } = updateService;
    public string DefaultLoginStateText => settingsService.Current.DefaultLoginStateOverride == LoginState.Offline
        ? "Offline" : "Online";

    private LoginState? _lastDefaultLoginPreference;
    private bool _defaultLoginPreferenceReported;
    public void RefreshDefaultLoginState()
    {
        OnPropertyChanged(nameof(DefaultLoginStateText));
        var value = settingsService.Current.DefaultLoginStateOverride;
        if (_defaultLoginPreferenceReported && _lastDefaultLoginPreference == value) return;
        _defaultLoginPreferenceReported = true;
        _lastDefaultLoginPreference = value;
        WeakReferenceMessenger.Default.Send(new DefaultLoginPreferenceChanged(value));
    }
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

    private string? _activeSteamId64;
    public event Action<SteamAccount?>? LiveSessionChanged;
    private readonly Helpers.AccountSwitchPresentation _switchPresentation = new();
    private SteamAccount? _displayedAccount;
    private SteamAccount? _previousSwitchAccount;
    private string _previousSwitchAvatar = string.Empty;

    public SteamAccount? ResolveObservedAccount(SteamAccount? observed) => _switchPresentation.Resolve(observed);

    public string TrayAccountStatusText => IsSwitchingAccount ? "Trocando conta..." : "Conta ativa";
    public int AccountGridDensityPercent => NormalizeAccountGridDensity(
        settingsService.Current.AccountGridDensityPercent);
    public string AccountGridDensityText => $"{AccountGridDensityPercent}%";
    public bool CanDecreaseAccountGridDensity => AccountGridDensityPercent > 50;
    public bool CanIncreaseAccountGridDensity => AccountGridDensityPercent < 100;
    public bool ShowAccountGridDensityControls => ActivePage == "Accounts";
    public int GameGridDensityPercent => NormalizeGameGridDensity(
        settingsService.Current.GameGridDensityPercent);
    public string GameGridDensityText => $"{GameGridDensityPercent}%";
    public bool CanDecreaseGameGridDensity => GameGridDensityPercent > 70;
    public bool CanIncreaseGameGridDensity => GameGridDensityPercent < 100;
    public bool ShowGameGridDensityControls => ActivePage == "Games"
        && settingsService.Current.GameViewMode == GameViewMode.Grid;

    partial void OnActivePageChanged(string value)
    {
        OnPropertyChanged(nameof(ShowAccountGridDensityControls));
        OnPropertyChanged(nameof(ShowGameGridDensityControls));
    }

    public void RefreshGameGridDensityVisibility()
        => OnPropertyChanged(nameof(ShowGameGridDensityControls));

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

    [RelayCommand]
    private async Task DecreaseGameGridDensityAsync()
        => await SetGameGridDensityAsync(70);

    [RelayCommand]
    private async Task IncreaseGameGridDensityAsync()
        => await SetGameGridDensityAsync(100);

    private async Task SetGameGridDensityAsync(int percent)
    {
        percent = NormalizeGameGridDensity(percent);
        if (GameGridDensityPercent == percent)
            return;

        settingsService.Current.GameGridDensityPercent = percent;
        await settingsService.SaveAsync(settingsService.Current);

        OnPropertyChanged(nameof(GameGridDensityPercent));
        OnPropertyChanged(nameof(GameGridDensityText));
        OnPropertyChanged(nameof(CanDecreaseGameGridDensity));
        OnPropertyChanged(nameof(CanIncreaseGameGridDensity));
        WeakReferenceMessenger.Default.Send(new GameGridDensityChanged());
    }

    private static int NormalizeGameGridDensity(int percent) => percent switch
    {
        <= 70 => 70,
        _ => 100,
    };

    partial void OnIsSwitchingAccountChanged(bool value)
        => OnPropertyChanged(nameof(TrayAccountStatusText));

    public void NotifyAccountSwitchStarted(SteamAccount target, string targetAvatarPath)
    {
        _previousSwitchAccount = _displayedAccount;
        _previousSwitchAvatar = ActiveAccountAvatarPath;
        _switchPresentation.Begin(target);
        IsSwitchingAccount = true;
        ApplyActiveAccount(target, targetAvatarPath);
        LiveSessionChanged?.Invoke(target);
    }

    public void NotifyAccountSwitchFinished(bool success = true)
    {
        if (success) _switchPresentation.Complete();
        else
        {
            _switchPresentation.Cancel();
            ApplyActiveAccount(_previousSwitchAccount, _previousSwitchAvatar);
            LiveSessionChanged?.Invoke(_previousSwitchAccount);
        }
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
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
        window.Focus();
    }

    public void UpdateStatusBar(string left, string right = "", bool showLoginToggle = false)
    {
        RefreshDefaultLoginState();
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
            var accounts = await accountService.GetAllAccountsAsync(ct);
            TrayAccounts = new ObservableCollection<SteamAccount>(accounts);
            ApplyActiveAccount(ResolveObservedAccount(accounts.FirstOrDefault(a => a.IsActive)));
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
        if (accountService.IsOperationInProgress || IsSwitchingAccount) return;
        var revision = _switchPresentation.Revision;
        var active = await accountService.GetActiveAccountAsync(ct);
        if (accountService.IsOperationInProgress || IsSwitchingAccount || revision != _switchPresentation.Revision) return;
        active = ResolveObservedAccount(active);
        var known = TrayAccounts.FirstOrDefault(a => a.UniqueKey == active?.UniqueKey);
        if (active is not null && known is not null)
        {
            active.CustomDisplayName = known.CustomDisplayName;
            active.CustomAvatarPath = known.CustomAvatarPath;
        }

        System.Diagnostics.Debug.WriteLine(
            $"[MainViewModel.RefreshActiveAccountAsync] active={(active is null ? "NULL" : active.AccountName)}");

        ApplyActiveAccount(active);
        LiveSessionChanged?.Invoke(active);
    }

    /// <summary>
    /// Aplica uma conta ativa ja conhecida em todas as UI dependententes.
    /// Usado tanto em InitializeAsync quanto apos troca iniciada pelo app,
    /// evitando re-ler do registry quando ja sabemos a resposta.
    /// </summary>
    public void ApplyActiveAccount(SteamAccount? active, string? avatarPath = null)
    {
        _displayedAccount = active;
        if (active is null)
        {
            _activeSteamId64 = null;
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

        var sameAccount = string.Equals(
            _activeSteamId64,
            active.UniqueKey,
            StringComparison.Ordinal);

        _activeSteamId64 = active.UniqueKey;
        HasActiveAccount = true;
        ActiveAccountName = active.DisplayName;
        StatusAccountName = active.DisplayName;
        TrayTooltip = $"Steam Switcher - {active.DisplayName}";
        TrayActiveAccountText = $"\u25CF {active.DisplayName}";
        ActiveAccountAvatarPath = ResolveTrayAvatarPath(
            active,
            avatarPath,
            sameAccount,
            ActiveAccountAvatarPath);

        StatusLoginState = active.WantsOfflineMode
            ? "Offline"
            : "Online";

        foreach (var a in TrayAccounts)
            a.IsActive = a.UniqueKey == active.UniqueKey;
    }

    private static string ResolveTrayAvatarPath(
        SteamAccount active,
        string? avatarPath,
        bool sameAccount,
        string currentAvatarPath)
    {
        if (!string.IsNullOrEmpty(avatarPath))
            return avatarPath;

        if (!string.IsNullOrEmpty(active.CustomAvatarPath))
            return active.CustomAvatarPath;

        if (sameAccount && !string.IsNullOrEmpty(currentAvatarPath))
            return currentAvatarPath;

        return string.Empty;
    }

    [RelayCommand]
    private async Task SwitchFromTrayAsync(SteamAccount account)
    {
        if (IsSwitchingAccount || accountService.IsOperationInProgress) return;
        if (!await CanChangeAccountAsync()) return;
        if (IsSwitchingAccount || accountService.IsOperationInProgress) return;
        snackbarService.Show(
            "Alternando conta",
            $"Entrando como {account.DisplayName}...",
            ControlAppearance.Secondary,
            null,
            TimeSpan.FromSeconds(3));

        try
        {
            NotifyAccountSwitchStarted(account, account.CustomAvatarPath ?? string.Empty);
            await accountService.SwitchAccountAsync(account);
            NotifyAccountSwitchFinished();
            ApplyActiveAccount(account);

            var settings = settingsService.Current;
            if (settings.AfterAccountSwitch == PostSwitchBehavior.Close)
                Application.Current.Shutdown();
            else if (settings.AfterAccountSwitch == PostSwitchBehavior.MinimizeToTray)
                ToggleWindow();
        }
        catch (Exception ex)
        {
            NotifyAccountSwitchFinished(success: false);
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
    private async Task ToggleLoginStateAsync()
    {
        var current = settingsService.Current;

        // Unset defaults to Online in the switch service and in the footer.
        var nextState = current.DefaultLoginStateOverride == LoginState.Offline
            ? LoginState.Online
            : LoginState.Offline;

        current.DefaultLoginStateOverride = nextState;

        await settingsService.SaveAsync(current);

        RefreshDefaultLoginState();
    }
}
