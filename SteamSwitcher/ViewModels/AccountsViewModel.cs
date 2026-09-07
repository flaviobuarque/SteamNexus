using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using SteamSwitcher.Core.Models;
using SteamSwitcher.Core.Services;
using SteamSwitcher.Helpers;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Data;
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
    ISteamInstallationService installationService,
    IServiceProvider serviceProvider,
    MainViewModel mainViewModel) : ObservableObject
{
    private readonly RangeObservableCollection<AccountCardViewModel> _accounts = [];
    private ICollectionView? _accountsView;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private AccountCardViewModel? _switchingAccount;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private int _filteredAccountsCount;
    [ObservableProperty] private bool _showFavoritesOnly;
    [ObservableProperty] private ObservableCollection<InstallationFilterOption> _installationFilters = [];
    [ObservableProperty] private InstallationFilterOption? _selectedInstallationFilter;

    public AccountSortMode AccountSortMode => settingsService.Current.AccountSortMode;
    public string SortLabel => AccountSortMode == AccountSortMode.RecentUsage
        ? "Recentes"
        : "Nome: A–Z";
    public bool IsRecentUsageSort => AccountSortMode == AccountSortMode.RecentUsage;
    public bool IsAlphabeticalSort => AccountSortMode == AccountSortMode.Alphabetical;
    public AccountViewMode AccountViewMode => settingsService.Current.AccountViewMode;
    public bool IsGridView => AccountViewMode == AccountViewMode.Grid;
    public bool IsCompactView => AccountViewMode == AccountViewMode.Compact;
    public bool IsGridContentVisible => !IsLoading && IsGridView;
    public bool IsCompactContentVisible => !IsLoading && IsCompactView;
    public double AccountCardScale => NormalizeGridDensity(
        settingsService.Current.AccountGridDensityPercent) switch
    {
        50 => 0.72,
        75 => 0.85,
        _ => 1d,
    };
    public Size AccountGridItemSize => new(
        Math.Ceiling(188 * AccountCardScale),
        Math.Ceiling(220 * AccountCardScale));
    public WpfToolkit.Controls.SpacingMode AccountGridSpacingMode =>
        WpfToolkit.Controls.SpacingMode.None;

    public void RefreshAccountGridDensity()
    {
        OnPropertyChanged(nameof(AccountCardScale));
        OnPropertyChanged(nameof(AccountGridItemSize));
        OnPropertyChanged(nameof(AccountGridSpacingMode));
    }

    private static int NormalizeGridDensity(int percent) => percent switch
    {
        <= 50 => 50,
        <= 75 => 75,
        _ => 100,
    };

    public ICollectionView AccountsView
    {
        get
        {
            if (_accountsView is null)
            {
                var view = CollectionViewSource.GetDefaultView(_accounts);
                view.Filter = AccountFilter;
                _accountsView = view;
                ApplySorting();
            }
            return _accountsView;
        }
    }

    private void ApplySorting()
    {
        if (_accountsView is null)
            return;

        using (_accountsView.DeferRefresh())
        {
            _accountsView.SortDescriptions.Clear();
            _accountsView.SortDescriptions.Add(new SortDescription(
                nameof(AccountCardViewModel.IsActive),
                ListSortDirection.Descending));
            _accountsView.SortDescriptions.Add(new SortDescription(
                nameof(AccountCardViewModel.IsFavorite),
                ListSortDirection.Descending));

            if (AccountSortMode == AccountSortMode.RecentUsage)
            {
                _accountsView.SortDescriptions.Add(new SortDescription(
                    nameof(AccountCardViewModel.Timestamp),
                    ListSortDirection.Descending));
            }
            else
            {
                _accountsView.SortDescriptions.Add(new SortDescription(
                    nameof(AccountCardViewModel.DisplayName),
                    ListSortDirection.Ascending));
            }

            // Desempate estável quando duas contas têm o mesmo nome ou timestamp.
            _accountsView.SortDescriptions.Add(new SortDescription(
                nameof(AccountCardViewModel.AccountName),
                ListSortDirection.Ascending));
        }
    }

    [RelayCommand]
    private async Task SortByRecentUsageAsync()
        => await SetSortModeAsync(AccountSortMode.RecentUsage);

    [RelayCommand]
    private async Task SortAlphabeticallyAsync()
        => await SetSortModeAsync(AccountSortMode.Alphabetical);

    private async Task SetSortModeAsync(AccountSortMode mode)
    {
        if (AccountSortMode == mode)
            return;

        settingsService.Current.AccountSortMode = mode;
        ApplySorting();

        OnPropertyChanged(nameof(AccountSortMode));
        OnPropertyChanged(nameof(SortLabel));
        OnPropertyChanged(nameof(IsRecentUsageSort));
        OnPropertyChanged(nameof(IsAlphabeticalSort));

        await settingsService.SaveAsync(settingsService.Current);
    }

    [RelayCommand]
    private async Task ShowGridViewAsync()
        => await SetAccountViewModeAsync(AccountViewMode.Grid);

    [RelayCommand]
    private async Task ShowCompactViewAsync()
        => await SetAccountViewModeAsync(AccountViewMode.Compact);

    private async Task SetAccountViewModeAsync(AccountViewMode mode)
    {
        if (AccountViewMode == mode) return;

        settingsService.Current.AccountViewMode = mode;
        OnPropertyChanged(nameof(AccountViewMode));
        OnPropertyChanged(nameof(IsGridView));
        OnPropertyChanged(nameof(IsCompactView));
        OnPropertyChanged(nameof(IsGridContentVisible));
        OnPropertyChanged(nameof(IsCompactContentVisible));
        await settingsService.SaveAsync(settingsService.Current);
    }

    public int AccountsCount => _accounts.Count;

    public bool HasNoAccounts => !IsLoading && _accounts.Count == 0;
    public bool HasNoSearchResults =>
        !IsLoading
        && _accounts.Count > 0
        && FilteredAccountsCount == 0
        && HasActiveFilters;

    private bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(SearchText)
        || ShowFavoritesOnly
        || SelectedInstallationFilter?.InstallationId is not null;

    public string AccountsCountText => HasActiveFilters
        ? $"{FilteredAccountsCount} de {FormatAccountCount(AccountsCount)}"
        : FormatAccountCount(AccountsCount);

    private readonly List<FileSystemWatcher> _vdfWatchers = [];
    private CancellationTokenSource? _vdfReloadCts;
    private CancellationTokenSource? _avatarLoadCts;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _avatarLoadGate = new(4, 4);
    private static readonly HttpClient AvatarHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private static readonly System.Text.RegularExpressions.Regex AvatarUrlRegex =
        new(@"<avatarFull><!\[CDATA\[(.+?)\]\]></avatarFull>",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private long _ignoreVdfChangesUntilUtcTicks;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        mainViewModel.LiveSessionChanged -= ApplyLiveSession;
        mainViewModel.LiveSessionChanged += ApplyLiveSession;
        await _initLock.WaitAsync(ct);
        try
        {
            var isInitialLoad = _accounts.Count == 0;
            var avatarLoadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var previousAvatarLoadCts = Interlocked.Exchange(ref _avatarLoadCts, avatarLoadCts);

            previousAvatarLoadCts?.Cancel();
            previousAvatarLoadCts?.Dispose();

            if (isInitialLoad)
                IsLoading = true;
            try
            {
                var rawAccounts = await accountService.GetAllAccountsAsync(ct);
                var activeAccount = rawAccounts.FirstOrDefault(a => a.IsActive);
                RefreshInstallationFilters();

                System.Diagnostics.Debug.WriteLine(
                    $"[AccountsViewModel.InitializeAsync] active={(activeAccount is null ? "NULL" : activeAccount.AccountName)}, rawCount={rawAccounts.Count}");

                foreach (var account in rawAccounts)
                {
                    account.IsActive = account.UniqueKey == activeAccount?.UniqueKey;
                }

                foreach (var account in rawAccounts)
                {
                    var ovr = await overrideService.GetOverrideAsync(account.UniqueKey)
                        ?? await overrideService.GetOverrideAsync(account.SteamId64);
                    if (ovr is not null)
                    {
                        account.CustomDisplayName = ovr.CustomDisplayName;
                        account.CustomAvatarPath = ovr.CustomAvatarPath;
                        account.LoginStateOverride = ovr.LoginStateOverride;
                        account.IsFavorite = ovr.IsFavorite;
                    }

                }

                ApplyAccountsIncrementally(rawAccounts);

                AccountsView.Refresh();
                UpdateAccountCounts();

                // Carrega gradualmente todas as contas na mesma ordem exibida.
                // Apenas quatro workers percorrem a fila; não são criadas N tarefas.
                var avatarQueue = AccountsView
                    .Cast<AccountCardViewModel>()
                    .ToList();
                _ = LoadAvatarsLazilyAsync(avatarQueue, avatarLoadCts.Token);

                StartWatchingLoginUsers();
            }
            finally
            {
                if (isInitialLoad)
                    IsLoading = false;
                RefreshStatusBar();
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    private bool AccountFilter(object item)
    {
        if (item is not AccountCardViewModel a) return false;
        if (ShowFavoritesOnly && !a.IsFavorite) return false;
        if (SelectedInstallationFilter?.InstallationId is { } installationId
            && a.InstallationId != installationId)
            return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        return a.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || a.AccountName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || a.InstallationName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || a.InstallationRootPath.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    partial void OnSearchTextChanged(string value)
    {
        AccountsView.Refresh();
        UpdateAccountCounts();
        RefreshStatusBar();
    }

    partial void OnShowFavoritesOnlyChanged(bool value) => RefreshAccountFilters();
    partial void OnSelectedInstallationFilterChanged(InstallationFilterOption? value)
        => RefreshAccountFilters();

    [RelayCommand]
    private void ToggleFavoritesFilter() => ShowFavoritesOnly = !ShowFavoritesOnly;

    [RelayCommand]
    private void ClearAccountFilters()
    {
        SearchText = string.Empty;
        ShowFavoritesOnly = false;
        SelectedInstallationFilter = InstallationFilters.FirstOrDefault();
        RefreshAccountFilters();
    }

    private void RefreshAccountFilters()
    {
        AccountsView.Refresh();
        UpdateAccountCounts();
        RefreshStatusBar();
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private async Task ToggleFavoriteAsync(AccountCardViewModel card)
    {
        card.IsFavorite = !card.IsFavorite;
        await SaveOrganizationAsync(card);
        ApplySorting();
        RefreshAccountFilters();
    }

    private async Task SaveOrganizationAsync(AccountCardViewModel card)
    {
        var current = await overrideService.GetOverrideAsync(card.UniqueKey)
            ?? await overrideService.GetOverrideAsync(card.SteamId64)
            ?? new AccountOverride();
        current.IsFavorite = card.IsFavorite;
        await overrideService.SaveOverrideAsync(card.UniqueKey, current);
    }

    private void UpdateAccountCounts()
    {
        FilteredAccountsCount = AccountsView.Cast<object>().Count();
        OnPropertyChanged(nameof(AccountsCount));
        OnPropertyChanged(nameof(HasNoAccounts));
        OnPropertyChanged(nameof(HasNoSearchResults));
        OnPropertyChanged(nameof(AccountsCountText));
    }

    private static string FormatAccountCount(int count)
        => count == 1 ? "1 conta" : $"{count} contas";

    private void RefreshInstallationFilters()
    {
        var previousId = SelectedInstallationFilter?.InstallationId;
        var options = new List<InstallationFilterOption>
        {
            new(null, "Todas as instalações", string.Empty),
        };
        options.AddRange(installationService.Installations
            .Where(i => i.IsValid)
            .Select(i => new InstallationFilterOption(i.Id, i.DisplayName, i.RootPath)));
        InstallationFilters = new ObservableCollection<InstallationFilterOption>(options);
        SelectedInstallationFilter = InstallationFilters.FirstOrDefault(i =>
                i.InstallationId == previousId)
            ?? InstallationFilters[0];
    }

    public void RefreshStatusBar()
    {
        mainViewModel.UpdateStatusBar(
            AccountsCountText,
            showLoginToggle: true);
    }

    private void StartWatchingLoginUsers()
    {
        foreach (var watcher in _vdfWatchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _vdfWatchers.Clear();

        foreach (var installation in installationService.Installations.Where(i => i.IsValid))
        {
            var dir = Path.GetDirectoryName(installation.LoginUsersPath);
            if (!Directory.Exists(dir)) continue;

            var watcher = new FileSystemWatcher(dir, Path.GetFileName(installation.LoginUsersPath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            watcher.Changed += (_, _) => QueueLoginUsersRefresh();
            watcher.Created += (_, _) => QueueLoginUsersRefresh();
            watcher.Renamed += (_, _) => QueueLoginUsersRefresh();
            _vdfWatchers.Add(watcher);
        }
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

            // Avisa o MainViewModel para re-ler a conta ativa do registry/VDF.
            WeakReferenceMessenger.Default.Send(new ActiveAccountChanged());
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
            var avatar = await Helpers.ImageLoader.LoadAvatarAsync(account.CustomAvatarPath);
            RunOnUi(() =>
            {
                card.AvatarPath = account.CustomAvatarPath;
                card.AvatarImage = avatar;

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

                    var match = AvatarUrlRegex.Match(xml);

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

                var avatar = await Helpers.ImageLoader.LoadAvatarAsync(localPath);

                RunOnUi(() =>
                {
                    card.AvatarPath = localPath;
                    card.AvatarImage = avatar;

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

    private void ApplyLiveSession(SteamAccount? active)
    {
        var changed = false;
        foreach (var card in _accounts)
        {
            var isActive = card.UniqueKey == active?.UniqueKey;
            changed |= card.IsActive != isActive;
            card.Account.IsActive = isActive;
            card.IsActive = isActive;
            if (isActive)
            {
                card.Account.WantsOfflineMode = active!.WantsOfflineMode;
                mainViewModel.ApplyActiveAccount(card.Account, card.AvatarPath);
            }
        }
        if (changed) _accountsView?.Refresh();
    }

    private void ApplyAccountsIncrementally(IReadOnlyList<SteamAccount> incoming)
    {
        var existingById = _accounts.ToDictionary(
            c => c.UniqueKey,
            StringComparer.Ordinal);
        var reconciled = new List<AccountCardViewModel>(incoming.Count);
        var showInstallationBadge = incoming.Any(account =>
            !string.IsNullOrWhiteSpace(account.InstallationId));

        foreach (var account in incoming)
        {
            if (existingById.TryGetValue(account.UniqueKey, out var existing))
            {
                existing.ApplySnapshot(account);
                existing.ShowInstallationBadge = showInstallationBadge;
                existing.PrepareAvatarReloadIfMissing();
                reconciled.Add(existing);
            }
            else
            {
                reconciled.Add(new AccountCardViewModel(account)
                {
                    ShowInstallationBadge = showInstallationBadge,
                });
            }
        }

        var membershipChanged = _accounts.Count != reconciled.Count
            || !_accounts.Select(c => c.UniqueKey)
                .SequenceEqual(reconciled.Select(c => c.UniqueKey),
                    StringComparer.Ordinal);

        if (membershipChanged)
            _accounts.ReplaceAll(reconciled);
    }

    private async Task LoadAvatarIfNeededAsync(
        AccountCardViewModel card,
        CancellationToken ct)
    {
        if (!card.TryBeginAvatarLoad())
            return;

        await LoadAvatarAsync(card, card.Account, ct);
    }

    private async Task LoadAvatarsLazilyAsync(
        IReadOnlyList<AccountCardViewModel> cards,
        CancellationToken ct)
    {
        try
        {
            await BoundedWorkQueue.RunAsync(
                cards,
                workerCount: 4,
                async (card, token) =>
                {
                    await LoadAvatarIfNeededAsync(card, token);
                    await Task.Yield();
                },
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Uma recarga mais recente substituiu esta fila.
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
        var previousActive = _accounts.FirstOrDefault(a => a.IsActive);

        if (previousActive is not null)
        {
            previousActive.IsActive = false;
            previousActive.Account.IsActive = false;
        }

        cardVm.IsActive = true;
        cardVm.Account.IsActive = true;
        AccountsView.Refresh();

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
            mainViewModel.ApplyActiveAccount(cardVm.Account, cardVm.AvatarPath);

            snackbarService.Show(
                "Conta alternada",
                $"Entrando como {cardVm.Account.DisplayName}"
                    + (mainViewModel.StatusLoginState == "Online"
                        ? " (Online)"
                        : $" ({mainViewModel.StatusLoginState})"),
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
            AccountsView.Refresh();
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
    private async Task ForgetAccountAsync(AccountCardViewModel cardVm)
    {
        if (cardVm.IsPendingRemoval) return;

        const double undoSeconds = 5;
        cardVm.IsPendingRemoval = true;
        cardVm.RemovalProgress = 100;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (cardVm.IsPendingRemoval && stopwatch.Elapsed.TotalSeconds < undoSeconds)
        {
            var remaining = Math.Max(0, undoSeconds - stopwatch.Elapsed.TotalSeconds);
            cardVm.RemovalProgress = remaining / undoSeconds * 100;
            cardVm.RemovalCountdownText = $"Removendo em {remaining:F1}s";
            await Task.Delay(100);
        }

        if (!cardVm.IsPendingRemoval) return; // usuário cancelou

        cardVm.RemovalProgress = 0;
        cardVm.RemovalCountdownText = "Removendo...";

        try
        {
            var wasActive = cardVm.IsActive;
            await accountService.ForgetAccountAsync(cardVm.Account);
            await overrideService.RemoveOverrideAsync(cardVm.UniqueKey);

            _accounts.Remove(cardVm);
            AccountsView.Refresh();
            UpdateAccountCounts();
            RefreshStatusBar();

            if (wasActive)
                mainViewModel.ApplyActiveAccount(null);

            snackbarService.Show(
                "Conta esquecida",
                $"{cardVm.DisplayName} foi removida deste computador.",
                ControlAppearance.Success,
                null,
                TimeSpan.FromSeconds(4));
        }
        catch (Exception ex)
        {
            cardVm.IsPendingRemoval = false;
            cardVm.RemovalProgress = 100;
            cardVm.RemovalCountdownText = string.Empty;
            snackbarService.Show(
                "Erro ao esquecer conta",
                ex.Message,
                ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(5));
        }
    }

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(HasNoAccounts));
        OnPropertyChanged(nameof(HasNoSearchResults));
        OnPropertyChanged(nameof(IsGridContentVisible));
        OnPropertyChanged(nameof(IsCompactContentVisible));
    }

    [RelayCommand]
    private void CancelForgetAccount(AccountCardViewModel cardVm)
    {
        cardVm.IsPendingRemoval = false;
        cardVm.RemovalProgress = 100;
        cardVm.RemovalCountdownText = string.Empty;
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
            AccountsView.Refresh();
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

public sealed record InstallationFilterOption(
    string? InstallationId,
    string DisplayName,
    string RootPath);
