using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SteamSwitcher.Core.Models;
using SteamSwitcher.Core.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SteamSwitcher.ViewModels;

public partial class AccountsViewModel(
    ISteamAccountService accountService,
    IAccountOverrideService overrideService,
    IImageCacheService imageCacheService,
    IWatchdogService watchdogService,
    ISnackbarService snackbarService,
    IAppSettingsService settingsService,
    IServiceProvider serviceProvider,
    MainViewModel mainViewModel) : ObservableObject
{
    [ObservableProperty] private ObservableCollection<AccountCardViewModel> _accounts = [];
    [ObservableProperty] private bool _isGridView = true;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private AccountCardViewModel? _switchingAccount;
    [ObservableProperty] private string _searchText = string.Empty;


    public bool HasNoAccounts => !IsLoading && Accounts.Count == 0;

    private FileSystemWatcher? _vdfWatcher;
    private CancellationTokenSource? _vdfReloadCts;
    private CancellationTokenSource? _avatarLoadCts;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _avatarLoadGate = new(4, 4);
    private static readonly HttpClient AvatarHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private long _ignoreVdfChangesUntilUtcTicks;
    private IReadOnlyList<AccountCardViewModel> _allAccounts = [];

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _initLock.WaitAsync(ct);
        try
        {
            var avatarLoadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var previousAvatarLoadCts = Interlocked.Exchange(ref _avatarLoadCts, avatarLoadCts);

            previousAvatarLoadCts?.Cancel();
            previousAvatarLoadCts?.Dispose();

            IsLoading = true;
            try
            {
                var demoAccounts = DebugDemoData.TryCreateAccountsFromArgs();

                var rawAccounts = demoAccounts
                    ?? await accountService.GetAccountsAsync(ct);

                var activeAccount = demoAccounts?.FirstOrDefault(a => a.IsActive)
                    ?? await accountService.GetActiveAccountAsync(ct);

                foreach (var account in rawAccounts)
                {
                    account.IsActive = account.SteamId64 == activeAccount?.SteamId64;
                }

                var cards = new List<AccountCardViewModel>();

                foreach (var account in rawAccounts)
                {
                    var ovr = await overrideService.GetOverrideAsync(account.SteamId64);
                    if (ovr is not null)
                    {
                        account.CustomDisplayName = ovr.CustomDisplayName;
                        account.CustomAvatarPath = ovr.CustomAvatarPath;
                        account.LoginStateOverride = ovr.LoginStateOverride;
                    }

                    var card = new AccountCardViewModel(account);
                    cards.Add(card);
                    _ = LoadAvatarAsync(card, account, avatarLoadCts.Token);
                }

                _allAccounts = cards;
                ApplyFilters();
                OnPropertyChanged(nameof(HasNoAccounts));

                StartWatchingLoginUsers();
            }
            finally
            {
                IsLoading = false;
                RefreshStatusBar();
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    private void ApplyFilters()
    {
        var filtered = _allAccounts
            .OrderByDescending(account => account.IsActive)
            .AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = filtered.Where(account =>
                account.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                account.AccountName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        Accounts = new ObservableCollection<AccountCardViewModel>(filtered);
    }

    public void RefreshStatusBar()
    {
        var active = Accounts.FirstOrDefault(a => a.IsActive);
        mainViewModel.UpdateStatusBar(
            $"{Accounts.Count} contas",
            showLoginToggle: true);
    }

    private void StartWatchingLoginUsers()
    {
        if (_vdfWatcher is not null)
            return;

        var locator = App.GetService<ISteamLocatorService>();
        var steamPath = locator.FindSteamInstallPath();
        if (string.IsNullOrEmpty(steamPath))
            return;

        var vdfPath = locator.GetLoginUsersVdfPath(steamPath);
        var dir = Path.GetDirectoryName(vdfPath);
        if (!Directory.Exists(dir))
            return;

        _vdfWatcher = new FileSystemWatcher(dir, "loginusers.vdf")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        _vdfWatcher.Changed += (_, _) => QueueLoginUsersRefresh();
    }

    private void QueueLoginUsersRefresh()
    {
        if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _ignoreVdfChangesUntilUtcTicks))
            return;

        var currentCts = new CancellationTokenSource();
        var previousCts = Interlocked.Exchange(ref _vdfReloadCts, currentCts);
        previousCts?.Cancel();

        _ = RefreshLoginUsersAfterDebounceAsync(currentCts);
    }

    private async Task RefreshLoginUsersAfterDebounceAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1500), cts.Token);

            if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _ignoreVdfChangesUntilUtcTicks))
                return;

            await System.Windows.Application.Current.Dispatcher
                .InvokeAsync(() => InitializeAsync())
                .Task
                .Unwrap();
        }
        catch (OperationCanceledException)
        {
            // Um evento mais recente já assumiu a atualização.
        }
        finally
        {
            if (ReferenceEquals(
                    Interlocked.CompareExchange(ref _vdfReloadCts, null, cts),
                    cts))
            {
                cts.Dispose();
            }
        }
    }

    private async Task LoadAvatarAsync(
    AccountCardViewModel card,
    SteamAccount account,
    CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(account.CustomAvatarPath))
        {
            RunOnUi(() =>
            {
                card.AvatarPath = account.CustomAvatarPath;

                if (card.IsActive)
                    mainViewModel.NotifyActiveAccountAvatarLoaded(account.CustomAvatarPath);
            });

            return;
        }

        try
        {
            await _avatarLoadGate.WaitAsync(ct);

            try
            {
                var avatarUrlKey = $"avatar-url:{account.SteamId64}";
                var avatarUrl = await imageCacheService.GetStringAsync(avatarUrlKey);

                // Só consulta o perfil Steam quando a URL não está mais no cache.
                if (string.IsNullOrWhiteSpace(avatarUrl))
                {
                    var profileUrl =
                        $"https://steamcommunity.com/profiles/{account.SteamId64}/?xml=1";

                    var xml = await AvatarHttp.GetStringAsync(profileUrl, ct);

                    var match = System.Text.RegularExpressions.Regex.Match(
                        xml,
                        @"<avatarFull><!\[CDATA\[(.+?)\]\]></avatarFull>");

                    if (!match.Success)
                        return;

                    avatarUrl = match.Groups[1].Value;

                    var expiryDays = account.IsActive
                        ? 1
                        : Math.Max(1, settingsService.Current.AvatarCacheExpiryDays);

                    await imageCacheService.SetStringAsync(
                        avatarUrlKey,
                        avatarUrl,
                        TimeSpan.FromDays(expiryDays));
                }

                // Se o arquivo já estiver cacheado, retorna sem rede.
                var localPath = await imageCacheService.GetCachedPathAsync(avatarUrl, ct);

                if (string.IsNullOrEmpty(localPath))
                    return;

                RunOnUi(() =>
                {
                    card.AvatarPath = localPath;

                    if (card.IsActive)
                        mainViewModel.NotifyActiveAccountAvatarLoaded(localPath);
                });
            }
            finally
            {
                _avatarLoadGate.Release();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Uma recarga mais recente tornou esta solicitação obsoleta.
        }
        catch
        {
            // Avatar não deve bloquear a tela de contas.
        }
    }

    [RelayCommand]
    private async Task SwitchAccountAsync(AccountCardViewModel cardVm)
    {
        if (SwitchingAccount is not null) return;
        if (cardVm.IsActive) return;

        SwitchingAccount = cardVm;
        cardVm.IsSwitching = true;

        // Feedback otimista: marca como ativa imediatamente na UI
        var previousActive = Accounts.FirstOrDefault(a => a.IsActive);

        if (previousActive is not null)
        {
            previousActive.IsActive = false;
            previousActive.Account.IsActive = false;
        }

        cardVm.IsActive = true;
        cardVm.Account.IsActive = true;

        try
        {
            mainViewModel.NotifyAccountSwitchStarted(
                cardVm.Account.DisplayName,
                cardVm.AvatarPath);

            watchdogService.BeginSwitch(cardVm.Account.SteamId64);

            snackbarService.Show(
                "Alternando conta",
                $"Encerrando Steam...",
                ControlAppearance.Secondary,
                null,
                TimeSpan.FromSeconds(10));

            Interlocked.Exchange(
                ref _ignoreVdfChangesUntilUtcTicks,
                DateTime.UtcNow.AddSeconds(8).Ticks);

            await accountService.SwitchAccountAsync(cardVm.Account);

            watchdogService.EndSwitch();

            mainViewModel.NotifyAccountSwitchFinished();
            mainViewModel.NotifyActiveAccountAvatarLoaded(cardVm.AvatarPath);
            mainViewModel.TrayTooltip = $"Steam Switcher — {cardVm.Account.DisplayName}";
            mainViewModel.TrayActiveAccountText = $"● {cardVm.Account.DisplayName}";

            snackbarService.Show(
                "Conta alternada",
                $"Entrando como {cardVm.Account.DisplayName}",
                ControlAppearance.Success,
                null,
                TimeSpan.FromSeconds(3));

            // Comportamento pós-troca
            ApplyPostSwitchBehavior(settingsService.Current.AfterAccountSwitch);
        }
        catch (Exception ex)
        {
            // Reverte UI
            cardVm.IsActive = false;
            if (previousActive is not null) previousActive.IsActive = true;
            mainViewModel.NotifyAccountSwitchFinished();
            watchdogService.EndSwitch();

            snackbarService.Show(
                "Erro ao trocar conta",
                ex.Message,
                ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(5));
        }
        finally
        {
            cardVm.IsSwitching = false;
            SwitchingAccount = null;
        }
    }

    [RelayCommand]
    private void ToggleView() => IsGridView = !IsGridView;

    [RelayCommand]
    private async Task ForgetAccountAsync(AccountCardViewModel cardVm)
    {
        // Undo disponível por 5s
        cardVm.IsPendingRemoval = true;
        await Task.Delay(5000);

        if (!cardVm.IsPendingRemoval) return; // usuário cancelou

        accountService.ForgetAccount(cardVm.Account);
        Accounts.Remove(cardVm);
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(HasNoAccounts));

    [RelayCommand]
    private void CancelForgetAccount(AccountCardViewModel cardVm)
    {
        cardVm.IsPendingRemoval = false;
    }

    private void ApplyPostSwitchBehavior(PostSwitchBehavior behavior)
    {
        switch (behavior)
        {
            case PostSwitchBehavior.MinimizeToTray:
                mainViewModel.HideWindowToTray();
                break;
            case PostSwitchBehavior.Close:
                System.Windows.Application.Current.Shutdown();
                break;
            case PostSwitchBehavior.KeepOpen:
            default:
                break;
        }
    }

    [RelayCommand]
    private async Task EditAccountAsync(AccountCardViewModel cardVm)
    {
        var editVm = serviceProvider.GetRequiredService<EditAccountViewModel>();
        var dialog = new Views.Dialogs.EditAccountDialog(editVm);
        dialog.Owner = System.Windows.Application.Current.MainWindow;
        dialog.LoadAccount(cardVm.Account, cardVm.AvatarPath);

        var result = dialog.ShowDialog();

        if (result == true)
        {
            cardVm.AvatarPath = cardVm.Account.CustomAvatarPath ?? cardVm.AvatarPath;
            cardVm.RefreshDisplayName();
        }
    }

    [RelayCommand]
    private void CopySteamId(AccountCardViewModel cardVm)
    {
        System.Windows.Clipboard.SetText(cardVm.Account.SteamId64);
        snackbarService.Show(
            "Copiado",
            $"SteamID64 copiado: {cardVm.Account.SteamId64}",
            ControlAppearance.Success,
            null,
            TimeSpan.FromSeconds(2));
    }

    [RelayCommand]
    private async Task AddAccountAsync()
    {
        snackbarService.Show(
            "Adicionar conta",
            "O Steam será aberto para login. Após entrar, a conta aparecerá automaticamente.",
            ControlAppearance.Secondary,
            null,
            TimeSpan.FromSeconds(5));

        await accountService.AddAccountAsync();
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }
}