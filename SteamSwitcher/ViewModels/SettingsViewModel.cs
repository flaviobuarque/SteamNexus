using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SteamSwitcher.Core;
using SteamSwitcher.Core.Models;
using SteamSwitcher.Core.Services;
using SteamSwitcher.Services.Updates;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SteamSwitcher.ViewModels;

public partial class SettingsViewModel(
    IAppSettingsService settingsService,
    ISystemService systemService,
    IImageCacheService imageCacheService,
    ISteamAccountService accountService,
    IAccountOverrideService accountOverrideService,
    ISteamInstallationService installationService,
    IUpdateService updateService,
    ISnackbarService snackbarService,
    MainViewModel mainViewModel) : ObservableObject
{
    private readonly MainViewModel _mainViewModel = mainViewModel;
    public IUpdateService UpdateService { get; } = updateService;

    [ObservableProperty] private AppTheme _theme;
    [ObservableProperty] private PostSwitchBehavior _afterAccountSwitch;
    [ObservableProperty] private PostSwitchBehavior _afterGameLaunch;
    [ObservableProperty] private LoginState? _defaultLoginStateOverride;
    [ObservableProperty] private bool _startSilent;
    [ObservableProperty] private bool _startAsAdmin;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private string _steamApiKey = string.Empty;
    [ObservableProperty] private string _steamGridDbApiKey = string.Empty;
    [ObservableProperty] private bool _hasUnsavedChanges;
    [ObservableProperty] private string _cacheSizeText = "Calculando...";
    [ObservableProperty] private bool _isGlobalHotkeyEnabled;
    [ObservableProperty] private HotkeyDefinition? _globalHotkey;
    [ObservableProperty] private bool _isCapturingHotkey;
    [ObservableProperty] private string _hotkeyDisplayText = "Nenhum atalho definido";
    [ObservableProperty] private string _hotkeyCaptureHint = string.Empty;
    [ObservableProperty] private bool _isCleaningOldAccounts;
    [ObservableProperty] private ObservableCollection<SteamInstallation> _steamInstallations = [];
    [ObservableProperty] private SteamInstallation? _selectedSteamInstallation;

    private readonly HashSet<Key> _pressedHotkeyKeys = [];
    private Key _capturedMainKey = Key.None;
    private HotkeyDefinition? _pendingHotkey;

    private AppSettings _original = new();
    public Func<string, string, string, string, bool>? ConfirmFunc { get; set; }

    partial void OnThemeChanged(AppTheme value) => CheckDirty();
    partial void OnAfterAccountSwitchChanged(PostSwitchBehavior value) => CheckDirty();
    partial void OnAfterGameLaunchChanged(PostSwitchBehavior value) => CheckDirty();
    partial void OnDefaultLoginStateOverrideChanged(LoginState? value) => CheckDirty();
    partial void OnStartSilentChanged(bool value) => CheckDirty();
    partial void OnStartAsAdminChanged(bool value) => CheckDirty();
    partial void OnStartWithWindowsChanged(bool value) => CheckDirty();
    partial void OnSteamApiKeyChanged(string value) => CheckDirty();
    partial void OnSteamGridDbApiKeyChanged(string value) => CheckDirty();
    partial void OnSelectedSteamInstallationChanged(SteamInstallation? value)
    {
        if (_initializing || value is null || value.Id == installationService.SelectedInstallation?.Id)
            return;
        _ = SelectSteamInstallationAsync(value);
    }

    private bool _initializing;

    private void CheckDirty()
    {
        if (_initializing) return;
        HasUnsavedChanges =
            Theme != _original.Theme ||
            AfterAccountSwitch != _original.AfterAccountSwitch ||
            AfterGameLaunch != _original.AfterGameLaunch ||
            DefaultLoginStateOverride != _original.DefaultLoginStateOverride ||
            StartSilent != _original.StartSilent ||
            StartAsAdmin != _original.StartAsAdmin ||
            StartWithWindows != systemService.GetStartWithWindows() ||
            (FeatureFlags.SteamWebApiKey && SteamApiKey != (_original.SteamApiKey ?? string.Empty)) ||
            IsGlobalHotkeyEnabled != _original.IsGlobalHotkeyEnabled ||
            !SameHotkey(GlobalHotkey, _original.GlobalHotkey) ||
            SteamGridDbApiKey != (_original.SteamGridDbApiKey ?? string.Empty);
        RefreshStatusBar();
    }

    public void Initialize()
    {
        _initializing = true;
        var s = settingsService.Current;
        _original = s;
        Theme = s.Theme;
        AfterAccountSwitch = s.AfterAccountSwitch;
        AfterGameLaunch = s.AfterGameLaunch;
        DefaultLoginStateOverride = s.DefaultLoginStateOverride;
        StartSilent = s.StartSilent;
        StartAsAdmin = s.StartAsAdmin;
        StartWithWindows = systemService.GetStartWithWindows();
        SteamApiKey = s.SteamApiKey ?? string.Empty;
        SteamGridDbApiKey = s.SteamGridDbApiKey ?? string.Empty;
        IsGlobalHotkeyEnabled = s.IsGlobalHotkeyEnabled;
        GlobalHotkey = s.GlobalHotkey;
        HotkeyDisplayText = s.GlobalHotkey?.DisplayText ?? "Nenhum atalho definido";
        HotkeyCaptureHint = string.Empty;
        RefreshSteamInstallations();
        HasUnsavedChanges = false;
        _initializing = false;
        RefreshStatusBar();
    }

    public void RefreshStatusBar()
    {
        var left = HasUnsavedChanges
            ? "Configurações com alterações não salvas"
            : "Configurações salvas";

        _mainViewModel.UpdateStatusBar(left, CacheSizeText);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var hadKey = string.IsNullOrWhiteSpace(_original.SteamGridDbApiKey);
        var settings = BuildSettingsFromCurrent();
        await settingsService.SaveAsync(settings);
        _mainViewModel.ApplyGlobalHotkey(
            settings.IsGlobalHotkeyEnabled && settings.GlobalHotkey?.IsValid == true
            ? settings.GlobalHotkey
            : null);
        systemService.SetStartWithWindows(StartWithWindows);
        App.ApplyTheme(Theme);
        _original = settings;
        HasUnsavedChanges = false;
        _mainViewModel.StatusLoginState =
            DefaultLoginStateOverride?.ToString() ?? "Não alterar";
        RefreshStatusBar();

        if (hadKey && !string.IsNullOrWhiteSpace(settings.SteamGridDbApiKey))
            WeakReferenceMessenger.Default.Send(new SteamGridDbKeyChanged());

        snackbarService.Show(
            "Configurações salvas",
            "As alterações foram aplicadas.",
            ControlAppearance.Success,
            null,
            TimeSpan.FromSeconds(3));
    }

    [RelayCommand]
    private async Task ApplyThemeOptionAsync(AppTheme theme)
    {
        Theme = theme;
        var settings = BuildSettingsFromCurrent();
        await settingsService.SaveAsync(settings);
        App.ApplyTheme(theme);
        _original = settings;
        HasUnsavedChanges = false;
    }

    public bool ConfirmNavigateAway()
    {
        if (!HasUnsavedChanges) return true;
        var confirmed = ConfirmFunc?.Invoke(
            "Alterações não salvas",
            "Há alterações não salvas. Deseja descartá-las e sair?",
            "Descartar e sair",
            "Voltar") ?? false;
        if (confirmed) { Initialize(); return true; }
        return false;
    }

    public async Task LoadCacheSizeAsync()
    {
        var bytes = await imageCacheService.GetCacheSizeAsync();
        CacheSizeText = bytes < 1024 * 1024
            ? $"{bytes / 1024.0:F1} KB"
            : $"{bytes / (1024.0 * 1024):F1} MB";
        RefreshStatusBar();
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        var before = await imageCacheService.GetCacheSizeAsync();
        await imageCacheService.ClearCacheAsync();
        var freed = before < 1024 * 1024
            ? $"{before / 1024.0:F1} KB"
            : $"{before / (1024.0 * 1024):F1} MB";

        CacheSizeText = "0 KB";
        WeakReferenceMessenger.Default.Send(new CacheCleared());

        snackbarService.Show(
            "Cache limpo",
            $"{freed} liberados.",
            ControlAppearance.Success,
            null,
            TimeSpan.FromSeconds(3));
    }

    private void RefreshSteamInstallations()
    {
        SteamInstallations = new ObservableCollection<SteamInstallation>(
            installationService.Installations);
        SelectedSteamInstallation = SteamInstallations.FirstOrDefault(i =>
            i.Id == installationService.SelectedInstallation?.Id);
    }

    private async Task SelectSteamInstallationAsync(SteamInstallation installation)
    {
        try
        {
            if (accountService.IsOperationInProgress)
                throw new InvalidOperationException("Aguarde a operação atual da Steam terminar.");
            await installationService.SelectAsync(installation.Id);
            RefreshSteamInstallations();
            WeakReferenceMessenger.Default.Send(new SteamInstallationChanged());
            snackbarService.Show("Instalação alterada", installation.RootPath,
                ControlAppearance.Success, null, TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            RefreshSteamInstallations();
            snackbarService.Show("Não foi possível alterar a Steam", ex.Message,
                ControlAppearance.Danger, null, TimeSpan.FromSeconds(5));
        }
    }

    [RelayCommand]
    private async Task BrowseSteamInstallationAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Selecione o Steam.exe",
            Filter = "Steam (Steam.exe)|Steam.exe|Executáveis (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            await installationService.AddCustomPathAsync(dialog.FileName);
            RefreshSteamInstallations();
            WeakReferenceMessenger.Default.Send(new SteamInstallationChanged());
            snackbarService.Show("Instalação adicionada", dialog.FileName,
                ControlAppearance.Success, null, TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            snackbarService.Show("Instalação inválida", ex.Message,
                ControlAppearance.Danger, null, TimeSpan.FromSeconds(5));
        }
    }

    [RelayCommand]
    private async Task DetectSteamInstallationsAsync()
    {
        await installationService.DiscoverAsync();
        RefreshSteamInstallations();
    }

    [RelayCommand]
    private void OpenSteamInstallationFolder(SteamInstallation? installation)
    {
        var path = installation?.RootPath ?? SelectedSteamInstallation?.RootPath;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    private async Task RemoveSteamInstallationAsync(SteamInstallation? installation)
    {
        installation ??= SelectedSteamInstallation;
        if (installation is not { IsCustom: true }) return;
        if (ConfirmFunc?.Invoke(
                "Remover instalação",
                $"Remover {installation.DisplayName} do SteamNexus? Nenhum arquivo da Steam será apagado.",
                "Remover",
                "Cancelar") == false)
            return;
        await installationService.RemoveCustomPathAsync(installation.Id);
        RefreshSteamInstallations();
        WeakReferenceMessenger.Default.Send(new SteamInstallationChanged());
    }

    [RelayCommand]
    private async Task RenameSteamInstallationAsync(SteamInstallation installation)
    {
        try
        {
            var dialog = new Views.Dialogs.RenameSteamInstallationDialog(
                installation.DisplayName,
                installation.RootPath)
            {
                Owner = Application.Current.MainWindow,
            };
            if (dialog.ShowDialog() != true) return;

            await installationService.RenameAsync(installation.Id, dialog.InstallationName);
            RefreshSteamInstallations();
            snackbarService.Show(
                "Nome da instalação salvo",
                SelectedSteamInstallation?.DisplayName ?? installation.RootPath,
                ControlAppearance.Success,
                null,
                TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            snackbarService.Show(
                "Não foi possível renomear a instalação",
                ex.Message,
                ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(5));
        }
    }

    [RelayCommand]
    private async Task SetDefaultSteamInstallationAsync(SteamInstallation installation)
        => await SelectSteamInstallationAsync(installation);

    [RelayCommand]
    private async Task RelocateSteamInstallationAsync(SteamInstallation installation)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Localizar {installation.DisplayName}",
            Filter = "Steam (Steam.exe)|Steam.exe|Executáveis (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            await installationService.RelocateAsync(installation.Id, dialog.FileName);
            RefreshSteamInstallations();
            WeakReferenceMessenger.Default.Send(new SteamInstallationChanged());
            snackbarService.Show(
                "Instalação localizada",
                dialog.FileName,
                ControlAppearance.Success,
                null,
                TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            snackbarService.Show(
                "Não foi possível localizar a instalação",
                ex.Message,
                ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(5));
        }
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        await UpdateService.CheckForUpdatesAsync();
        snackbarService.Show(
            UpdateService.IsUpdateAvailable
                ? "Atualização disponível"
                : "Atualizações",
            UpdateService.StatusText,
            string.IsNullOrEmpty(UpdateService.ErrorText)
                ? ControlAppearance.Success
                : ControlAppearance.Danger,
            null,
            TimeSpan.FromSeconds(4));
    }

    [RelayCommand]
    private async Task DownloadUpdateAsync()
    {
        await UpdateService.DownloadUpdateAsync();
        if (!string.IsNullOrEmpty(UpdateService.ErrorText))
        {
            snackbarService.Show(
                "Falha ao baixar atualização",
                UpdateService.ErrorText,
                ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(5));
        }
    }

    [RelayCommand]
    private void ApplyUpdateAndRestart()
    {
        if (HasUnsavedChanges)
        {
            snackbarService.Show(
                "Salve as configurações primeiro",
                "Há alterações pendentes antes de reiniciar para atualizar.",
                ControlAppearance.Caution,
                null,
                TimeSpan.FromSeconds(4));
            return;
        }

        UpdateService.ApplyUpdateAndRestart();
    }

    public Task<IReadOnlyList<SteamAccount>> GetAccountsForCleanupAsync(
        CancellationToken ct = default) => accountService.GetAllAccountsAsync(ct);

    public async Task CleanupOldAccountsAsync(
        IReadOnlyList<SteamAccount> accounts,
        CancellationToken ct = default)
    {
        if (accounts.Count == 0 || IsCleaningOldAccounts) return;

        IsCleaningOldAccounts = true;
        try
        {
            var active = await accountService.GetActiveAccountAsync(ct);
            var targets = accounts
                .Where(account => !string.Equals(
                    account.UniqueKey,
                    active?.UniqueKey,
                    StringComparison.Ordinal))
                .ToList();

            var removedIds = await accountService.ForgetAccountsAsync(
                targets,
                ct);

            foreach (var steamId64 in removedIds)
                await accountOverrideService.RemoveOverrideAsync(steamId64);

            snackbarService.Show(
                "Limpeza concluída",
                removedIds.Count == 1
                    ? "1 conta antiga foi removida."
                    : $"{removedIds.Count} contas antigas foram removidas.",
                ControlAppearance.Success,
                null,
                TimeSpan.FromSeconds(4));
        }
        catch (Exception ex)
        {
            snackbarService.Show(
                "Erro ao limpar contas",
                ex.Message,
                ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(5));
        }
        finally
        {
            IsCleaningOldAccounts = false;
        }
    }

    [RelayCommand]
    private void ResetAppData()
    {
        var confirmed = ConfirmFunc?.Invoke(
            "Apagar dados do Steam Switcher",
            "Isso apagará configurações, cache, overrides e onboarding. Nenhum dado da Steam será afetado. O app será reiniciado.\n\nContinuar?",
            "Apagar e reiniciar",
            "Cancelar") ?? false;
        if (!confirmed) return;

        var appDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteamSwitcher");

        try
        {
            if (Directory.Exists(appDir))
                Directory.Delete(appDir, recursive: true);

            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(exePath))
                Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            snackbarService.Show("Erro ao apagar dados", ex.Message,
                ControlAppearance.Danger, null, TimeSpan.FromSeconds(5));
        }
    }

    private AppSettings BuildSettingsFromCurrent()
    {
        var current = settingsService.Current;
        return new AppSettings
        {
            Theme = Theme,
            AccountSortMode = current.AccountSortMode,
            AccountViewMode = current.AccountViewMode,
            AccountGridDensityPercent = current.AccountGridDensityPercent,
            GameSortMode = current.GameSortMode,
            GameViewMode = current.GameViewMode,
            AfterAccountSwitch = AfterAccountSwitch,
            AfterGameLaunch = AfterGameLaunch,
            DefaultLoginStateOverride = DefaultLoginStateOverride,
            StartSilent = StartSilent,
            StartAsAdmin = StartAsAdmin,
            SteamApiKey = FeatureFlags.SteamWebApiKey
                ? (string.IsNullOrWhiteSpace(SteamApiKey) ? null : SteamApiKey)
                : null,
            SteamInstallPath = current.SteamInstallPath,
            KnownSteamInstallPaths = [.. current.KnownSteamInstallPaths],
            SteamInstallationNames = new Dictionary<string, string>(
                current.SteamInstallationNames,
                StringComparer.OrdinalIgnoreCase),
            AvatarCacheExpiryDays = current.AvatarCacheExpiryDays,
            CoverCacheExpiryDays = current.CoverCacheExpiryDays,
            SteamGridDbApiKey = string.IsNullOrWhiteSpace(SteamGridDbApiKey) ? null : SteamGridDbApiKey,
            IsGlobalHotkeyEnabled = IsGlobalHotkeyEnabled,
            GlobalHotkey = GlobalHotkey is null ? null : new HotkeyDefinition
            {
                Modifiers = GlobalHotkey.Modifiers,
                VirtualKey = GlobalHotkey.VirtualKey,
                KeyName = GlobalHotkey.KeyName
            },
        };
    }

    partial void OnIsGlobalHotkeyEnabledChanged(bool value)
    {
        if (!value)
            CancelHotkeyCapture();

        CheckDirty();
    }

    partial void OnGlobalHotkeyChanged(HotkeyDefinition? value)
    {
        HotkeyDisplayText = value?.DisplayText ?? "Nenhum atalho definido";
        OnPropertyChanged(nameof(HotkeyButtonText));
        CheckDirty();
    }

    public void BeginHotkeyCapture()
    {
        if (!IsGlobalHotkeyEnabled)
            return;

        _pressedHotkeyKeys.Clear();
        _pendingHotkey = null;
        _capturedMainKey = Key.None;

        IsCapturingHotkey = true;
        HotkeyCaptureHint = "Pressione Ctrl, Shift, Alt ou Win junto com uma tecla.";
    }

    public void CancelHotkeyCapture()
    {
        _pressedHotkeyKeys.Clear();
        _pendingHotkey = null;
        _capturedMainKey = Key.None;
        IsCapturingHotkey = false;
        HotkeyCaptureHint = string.Empty;
    }

    public bool CaptureHotkeyPreviewKeyDown(KeyEventArgs e)
    {
        if (!IsCapturingHotkey)
            return false;

        var key = NormalizeKey(e.Key == Key.System ? e.SystemKey : e.Key);

        // Ignora auto-repeat.
        if (!_pressedHotkeyKeys.Add(key))
            return true;

        // Apenas modificador: mantém a captura aberta.
        if (IsModifier(key))
            return true;

        var modifiers = GetPressedModifiers();

        // Um atalho global precisa conter modificador + tecla principal.
        if (modifiers == HotkeyModifiers.None)
        {
            HotkeyCaptureHint = "Inclua pelo menos Ctrl, Shift, Alt ou Win.";
            return true;
        }

        _capturedMainKey = key;
        _pendingHotkey = new HotkeyDefinition
        {
            Modifiers = modifiers,
            VirtualKey = KeyInterop.VirtualKeyFromKey(key),
            KeyName = key.ToString()
        };

        HotkeyDisplayText = $"{_pendingHotkey.DisplayText} — solte a tecla principal para confirmar";
        return true;
    }

    public bool CaptureHotkeyPreviewKeyUp(KeyEventArgs e)
    {
        if (!IsCapturingHotkey)
            return false;

        var key = NormalizeKey(e.Key == Key.System ? e.SystemKey : e.Key);
        _pressedHotkeyKeys.Remove(key);

        // Só confirma ao soltar a tecla principal.
        if (key != _capturedMainKey || _pendingHotkey is null)
            return true;

        GlobalHotkey = _pendingHotkey;
        HotkeyDisplayText = GlobalHotkey.DisplayText;

        _pendingHotkey = null;
        _capturedMainKey = Key.None;
        IsCapturingHotkey = false;
        HotkeyCaptureHint = "Atalho definido.";
        return true;
    }

    private HotkeyModifiers GetPressedModifiers()
    {
        var result = HotkeyModifiers.None;

        if (_pressedHotkeyKeys.Contains(Key.LeftCtrl))
            result |= HotkeyModifiers.Ctrl;

        if (_pressedHotkeyKeys.Contains(Key.LeftShift))
            result |= HotkeyModifiers.Shift;

        if (_pressedHotkeyKeys.Contains(Key.LeftAlt))
            result |= HotkeyModifiers.Alt;

        if (_pressedHotkeyKeys.Contains(Key.LWin))
            result |= HotkeyModifiers.Win;

        return result;
    }

    private static bool IsModifier(Key key) =>
        key is Key.LeftCtrl or Key.LeftShift or Key.LeftAlt or Key.LWin;

    private static Key NormalizeKey(Key key) => key switch
    {
        Key.LeftCtrl or Key.RightCtrl => Key.LeftCtrl,
        Key.LeftShift or Key.RightShift => Key.LeftShift,
        Key.LeftAlt or Key.RightAlt => Key.LeftAlt,
        Key.LWin or Key.RWin => Key.LWin,
        _ => key
    };

    private static bool SameHotkey(HotkeyDefinition? left, HotkeyDefinition? right) =>
        left?.Modifiers == right?.Modifiers &&
        left?.VirtualKey == right?.VirtualKey;
    public string HotkeyButtonText =>
    GlobalHotkey?.IsValid == true
        ? "Alterar"
        : "Definir";

    [RelayCommand]
    private void Undo() => Initialize();
}
