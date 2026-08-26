using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SteamSwitcher.Core.Helpers;
using SteamSwitcher.Core.Models;
using SteamSwitcher.Core.Services;
using SteamSwitcher.Helpers;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SteamSwitcher.ViewModels;

public partial class GamesViewModel(
    ISteamGameService gameService,
    ISteamAccountService accountService,
    IImageCacheService imageCacheService,
    ISnackbarService snackbarService,
    IAppSettingsService settingsService,
    MainViewModel mainViewModel) : ObservableObject
{
    private IReadOnlyList<GameCardViewModel> _allGames = [];
    private IReadOnlyList<GameCardViewModel> _filteredGames = [];
    private const int GamesPerPage = 60;
    private IReadOnlyList<SteamAccount> _allAccounts = [];
    private readonly SemaphoreSlim _coverLoadGate = new(6, 6);
    private CancellationTokenSource? _visibleCoverLoadCts;
    private readonly ConcurrentDictionary<string, Lazy<Task>> _activeCoverLoads =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private bool _initialized;
    private CancellationTokenSource? _gameSearchDebounceCts;
    private CancellationTokenSource? _accountSearchDebounceCts;
    private int _launchOperationActive;
    private readonly SemaphoreSlim _libraryRefreshGate = new(1, 1);
    private readonly List<FileSystemWatcher> _libraryWatchers = [];
    private CancellationTokenSource? _libraryRefreshDebounceCts;
    private Dictionary<string, string> _savedOwners = new(StringComparer.Ordinal);
    private HashSet<string> _favoriteGameIds = new(StringComparer.Ordinal);
    private static readonly HttpClient FilterAvatarHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };
    private static readonly System.Text.RegularExpressions.Regex AvatarUrlRegex =
        new(@"<avatarFull><!\[CDATA\[(.+?)\]\]></avatarFull>",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    [ObservableProperty] private ObservableCollection<GameCardViewModel> _games = [];
    [ObservableProperty] private ObservableCollection<AccountCardViewModel> _filterAccounts = [];
    [ObservableProperty] private AccountCardViewModel? _selectedFilterAccount;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isLoading;

    [ObservableProperty] private ObservableCollection<AccountCardViewModel> _visibleFilterAccounts = [];
    [ObservableProperty] private string _filterAccountSearchText = string.Empty;
    [ObservableProperty] private bool _isAccountFilterOpen;
    [ObservableProperty] private bool _isGameLaunchInProgress;

    public GameSortMode GameSortMode => settingsService.Current.GameSortMode;
    public string GameSortLabel => GameSortMode switch
    {
        GameSortMode.MostPlayed => "Mais jogados",
        GameSortMode.LargestSize => "Maior tamanho",
        _ => "Nome: A–Z"
    };
    public bool IsAlphabeticalGameSort => GameSortMode == GameSortMode.Alphabetical;
    public bool IsMostPlayedGameSort => GameSortMode == GameSortMode.MostPlayed;
    public bool IsLargestSizeGameSort => GameSortMode == GameSortMode.LargestSize;
    public GameViewMode GameViewMode => settingsService.Current.GameViewMode;
    public bool IsGameGridView => GameViewMode == GameViewMode.Grid;
    public bool IsGameCompactView => GameViewMode == GameViewMode.Compact;
    public bool IsGameGridContentVisible => HasFilteredGames && IsGameGridView;
    public bool IsGameCompactContentVisible => HasFilteredGames && IsGameCompactView;
    public double GameCardScale => NormalizeGridDensity(
        settingsService.Current.GameGridDensityPercent) switch
    {
        70 => 0.80,
        85 => 0.90,
        _ => 1d,
    };
    public Size GameGridItemSize => new(
        Math.Ceiling(172 * GameCardScale),
        Math.Ceiling(320 * GameCardScale));

    public void RefreshGameGridDensity()
    {
        OnPropertyChanged(nameof(GameCardScale));
        OnPropertyChanged(nameof(GameGridItemSize));
    }

    private static int NormalizeGridDensity(int percent) => percent switch
    {
        <= 70 => 70,
        <= 85 => 85,
        _ => 100,
    };

    [ObservableProperty] private int _currentPage = 1;

    partial void OnSearchTextChanged(string value)
    {
        var nextCts = new CancellationTokenSource();
        var previousCts = Interlocked.Exchange(ref _gameSearchDebounceCts, nextCts);
        previousCts?.Cancel();
        previousCts?.Dispose();
        _ = ApplyGameSearchDebouncedAsync(nextCts.Token);
    }
    partial void OnSelectedFilterAccountChanged(AccountCardViewModel? value) => ApplyFilters();
    partial void OnIsAccountFilterOpenChanged(bool value)
    {
        if (value)
            StartFilterAvatarLoading();
    }
    public bool HasNoInstalledGames => !IsLoading && _allGames.Count == 0;
    public bool HasNoFilterResults =>
        !IsLoading && _allGames.Count > 0 && _filteredGames.Count == 0;
    public bool HasFilteredGames => !IsLoading && _filteredGames.Count > 0;

    partial void OnIsLoadingChanged(bool value) => NotifyEmptyStates();
    partial void OnGamesChanged(ObservableCollection<GameCardViewModel> value) => NotifyEmptyStates();

    public int TotalPages => Math.Max(
    1,
    (int)Math.Ceiling(_filteredGames.Count / (double)GamesPerPage));

    public bool CanGoPreviousPage => CurrentPage > 1;
    public bool CanGoNextPage => CurrentPage < TotalPages;

    public string PageText =>
        _filteredGames.Count == 0
            ? "Nenhum jogo"
            : $"Página {CurrentPage} de {TotalPages} · {_filteredGames.Count} jogos";

    partial void OnCurrentPageChanged(int value)
    {
        UpdateVisiblePage();
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized)
            return;

        await _initializeGate.WaitAsync(ct);
        try
        {
            if (_initialized)
                return;

            await InitializeCoreAsync(ct);
            _initialized = true;
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    private async Task InitializeCoreAsync(CancellationToken ct)
    {
        if (!WeakReferenceMessenger.Default.IsRegistered<SteamGridDbKeyChanged>(this))
            WeakReferenceMessenger.Default.Register<SteamGridDbKeyChanged>(this, async (_, _) =>
                await RetryMissingCoversAsync());

        if (!WeakReferenceMessenger.Default.IsRegistered<CacheCleared>(this))
            WeakReferenceMessenger.Default.Register<CacheCleared>(this, (_, _) =>
            {
                RunOnUi(() =>
                {
                    foreach (var card in _allGames)
                    {
                        card.CoverPath = string.Empty;
                        card.CoverImage = null;
                        card.CoverMissing = false;
                    }

                    foreach (var account in FilterAccounts)
                    {
                        account.AvatarPath = string.Empty;
                        account.AvatarImage = null;
                        account.PrepareAvatarReloadIfMissing();
                    }

                    // Re-kickoff para a página visível: após o reset, capas que
                    // existiam voltam a faltar e disparam nova carga.
                    KickoffMissingCoverLoads(Games);
                });
            });

        IsLoading = true;
        try
        {
            var accounts = await accountService.GetAllAccountsAsync(ct);
            _allAccounts = accounts;

            // Monta lista de filtros
            FilterAccounts = new ObservableCollection<AccountCardViewModel>(
                accounts.Prepend(new SteamAccount
                {
                    SteamId64 = string.Empty,
                    AccountName = "Sem filtro de conta",
                    PersonaName = "Todas as contas"
                }).Select(account => new AccountCardViewModel(account)));
            SelectedFilterAccount = FilterAccounts.First();

            VisibleFilterAccounts = new ObservableCollection<AccountCardViewModel>(FilterAccounts);

            // Carrega jogos
            var rawGames = await gameService.GetInstalledGamesAsync(accounts, ct);
            _favoriteGameIds = await LoadFavoriteGameIdsAsync();
            var cards = rawGames
                .Select(game => new GameCardViewModel(
                    game,
                    _favoriteGameIds.Contains(game.UniqueKey)
                        || _favoriteGameIds.Contains(game.AppId)))
                .ToList();
            _allGames = cards;

            _savedOwners = await LoadGameOwnersAsync();
            if (_savedOwners.Count > 0)
            {
                foreach (var card in cards)
                {
                    if (_savedOwners.TryGetValue(card.Game.UniqueKey, out var savedId)
                        || _savedOwners.TryGetValue(card.Game.AppId, out savedId))
                    {
                        var owner = _allAccounts.FirstOrDefault(a => a.UniqueKey == savedId)
                            ?? _allAccounts.FirstOrDefault(a =>
                                a.InstallationId == card.Game.InstallationId
                                && a.SteamId64 == savedId);
                        if (owner is not null)
                        {
                            card.Game.OwnerAccount = owner;
                            card.Game.OwnerSteamId64 = savedId;
                            card.OnOwnerChanged();
                        }
                    }
                }
            }

            ApplyFilters();
            StartLibraryWatchers();

        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnFilterAccountSearchTextChanged(string value)
    {
        var nextCts = new CancellationTokenSource();
        var previousCts = Interlocked.Exchange(ref _accountSearchDebounceCts, nextCts);
        previousCts?.Cancel();
        previousCts?.Dispose();
        _ = ApplyAccountSearchDebouncedAsync(nextCts.Token);
    }

    private async Task ApplyGameSearchDebouncedAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
            if (!ct.IsCancellationRequested)
                RunOnUi(() => ApplyFilters());
        }
        catch (OperationCanceledException)
        {
            // Uma tecla mais recente substituiu esta busca.
        }
    }

    private async Task ApplyAccountSearchDebouncedAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(180), ct);
            if (!ct.IsCancellationRequested)
                RunOnUi(ApplyAccountFilterSearch);
        }
        catch (OperationCanceledException)
        {
            // Uma tecla mais recente substituiu esta busca.
        }
    }

    [RelayCommand]
    private void SelectGameFilterAccount(AccountCardViewModel account)
    {
        SelectedFilterAccount = account;
        IsAccountFilterOpen = false;
        FilterAccountSearchText = string.Empty;
    }

    private void ApplyAccountFilterSearch()
    {
        var filtered = FilterAccounts.Where(account =>
            string.IsNullOrWhiteSpace(FilterAccountSearchText) ||
            account.DisplayName.Contains(FilterAccountSearchText,
                StringComparison.OrdinalIgnoreCase) ||
            account.AccountName.Contains(FilterAccountSearchText,
                StringComparison.OrdinalIgnoreCase));

        VisibleFilterAccounts = new ObservableCollection<AccountCardViewModel>(filtered);
    }

    private void StartFilterAvatarLoading()
    {
        var accounts = FilterAccounts
            .Where(account => !string.IsNullOrEmpty(account.SteamId64))
            .ToList();

        if (accounts.Count == 0)
            return;

        _ = LoadFilterAvatarsAsync(accounts);
    }

    private async Task LoadFilterAvatarsAsync(
        IReadOnlyList<AccountCardViewModel> accounts)
    {
        try
        {
            await BoundedWorkQueue.RunAsync(
                accounts,
                workerCount: 4,
                LoadFilterAvatarIfNeededAsync,
                CancellationToken.None);
        }
        catch
        {
            // Uma falha de imagem não deve impedir o uso do filtro.
        }
    }

    private async Task LoadFilterAvatarIfNeededAsync(
        AccountCardViewModel card,
        CancellationToken ct)
    {
        if (!card.TryBeginAvatarLoad())
            return;

        try
        {
            var account = card.Account;
            string? localPath;

            if (!string.IsNullOrWhiteSpace(account.CustomAvatarPath))
            {
                localPath = account.CustomAvatarPath;
            }
            else
            {
                var avatarUrlKey = $"avatar-url:{account.SteamId64}";
                var avatarUrl = await imageCacheService.GetStringAsync(avatarUrlKey);

                if (string.IsNullOrWhiteSpace(avatarUrl))
                {
                    var profileUrl =
                        $"https://steamcommunity.com/profiles/{account.SteamId64}/?xml=1";
                    var xml = await FilterAvatarHttp.GetStringAsync(profileUrl, ct);
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

                localPath = await imageCacheService.GetCachedPathAsync(avatarUrl, ct);
            }

            if (string.IsNullOrWhiteSpace(localPath))
                return;

            var avatar = await Helpers.ImageLoader.LoadAvatarAsync(localPath);
            if (avatar is null)
                return;

            RunOnUi(() =>
            {
                card.AvatarPath = localPath;
                card.AvatarImage = avatar;
            });

        if (!WeakReferenceMessenger.Default.IsRegistered<SteamInstallationChanged>(this))
            WeakReferenceMessenger.Default.Register<SteamInstallationChanged>(this, async (_, _) =>
            {
                _visibleCoverLoadCts?.Cancel();
                _gameSearchDebounceCts?.Cancel();
                _accountSearchDebounceCts?.Cancel();
                _initialized = false;
                await System.Windows.Application.Current.Dispatcher
                    .InvokeAsync(() => InitializeAsync())
                    .Task
                    .Unwrap();
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A fila foi cancelada durante o fechamento do aplicativo.
        }
        catch
        {
            // Mantém o ícone de fallback para perfis indisponíveis.
        }
    }

    private async Task RetryMissingCoversAsync()
    {
        var missing = _allGames.Where(c => c.CoverMissing).ToList();
        foreach (var card in missing)
        {
            card.CoverMissing = false;
            await LoadGameDataAsync(card, CancellationToken.None);
        }
    }

    [RelayCommand]
    private async Task AddCoverAsync(GameCardViewModel cardVm)
    {
        var dialog = new SteamSwitcher.Views.Dialogs.AddGameCoverDialog(cardVm.Game.Name)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true || string.IsNullOrEmpty(dialog.SelectedImagePath))
            return;

        // Comprime e copia para covers_manual\<appId>.jpg
        var destPath = System.IO.Path.Combine(
            SteamSwitcher.Core.Services.SteamGameService.ManualCoversDir,
            $"{cardVm.Game.AppId}.jpg");

        var compressed = await Task.Run(() =>
            SteamSwitcher.Core.Helpers.CoverCompressor.TryCompress(
                dialog.SelectedImagePath, destPath));

        if (!compressed || !File.Exists(destPath))
        {
            snackbarService.Show(
                "Erro",
                "Não foi possível processar a imagem.",
                ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(4));
            return;
        }

        cardVm.Game.ManualCoverPath = destPath;
        await gameService.SetManualCoverAsync(cardVm.Game.AppId, destPath);

        // Aplica imediatamente na UI.
        var img = await Helpers.ImageLoader.LoadCoverAsync(destPath);
        RunOnUi(() =>
        {
            cardVm.CoverPath = destPath;
            cardVm.CoverImage = img;
            cardVm.CoverMissing = false;
        });

        snackbarService.Show(
            "Capa adicionada",
            $"Capa manual definida para {cardVm.Game.Name}.",
            ControlAppearance.Success,
            null,
            TimeSpan.FromSeconds(3));
    }

    [RelayCommand]
    private async Task RemoveManualCoverAsync(GameCardViewModel cardVm)
    {
        await gameService.ClearManualCoverAsync(cardVm.Game.AppId);
        cardVm.Game.ManualCoverPath = null;

        // Limpa UI e re-busca automaticamente (Opcao A).
        RunOnUi(() =>
        {
            cardVm.CoverPath = string.Empty;
            cardVm.CoverImage = null;
            cardVm.CoverMissing = false;
        });

        _ = LoadGameDataAsync(cardVm, CancellationToken.None);
    }

    [RelayCommand]
    private void OpenSteamGridDbSettings()
    {
        WeakReferenceMessenger.Default.Send(new NavigateToSettingsRequested());
    }

    private async Task LoadGameDataAsync(GameCardViewModel card, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var candidate = new Lazy<Task>(
                () => LoadGameDataCoreAsync(card, ct),
                LazyThreadSafetyMode.ExecutionAndPublication);
            var active = _activeCoverLoads.GetOrAdd(card.Game.AppId, candidate);

            try
            {
                await active.Value.WaitAsync(ct);
                return;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // A operação compartilhada pertencia à página anterior. Remove
                // a entrada concluída e repete usando o token da página atual.
            }
            finally
            {
                if (active.IsValueCreated && active.Value.IsCompleted)
                {
                    _activeCoverLoads.TryRemove(
                        new KeyValuePair<string, Lazy<Task>>(card.Game.AppId, active));
                }
            }

            await Task.Yield();
        }
    }

    private async Task LoadGameDataCoreAsync(GameCardViewModel card, CancellationToken ct)
    {
        // Limita concorrência: múltiplos cards sem capa não disparam todas
        // as HTTPs de uma vez.
        await _coverLoadGate.WaitAsync(ct);
        RunOnUi(() => card.IsCoverLoading = true);

        try
        {
            // Ao retornar para uma página, reutiliza o caminho já resolvido sem
            // repetir consultas de rede ou de cache persistente.
            if (!string.IsNullOrEmpty(card.CoverPath) && File.Exists(card.CoverPath))
            {
                var knownImg = await Helpers.ImageLoader.LoadCoverAsync(card.CoverPath);
                RunOnUi(() => card.CoverImage = knownImg);
                return;
            }

            // Capa manual tem precedencia sobre cache e SteamGridDB.
            if (!string.IsNullOrEmpty(card.Game.ManualCoverPath)
                && File.Exists(card.Game.ManualCoverPath))
            {
                var manualImg = await Helpers.ImageLoader.LoadCoverAsync(card.Game.ManualCoverPath);
                RunOnUi(() =>
                {
                    card.CoverPath = card.Game.ManualCoverPath!;
                    card.CoverImage = manualImg;
                    card.CoverMissing = false;
                });
                return;
            }

            // Busca capa
            var steamUrl = $"https://cdn.akamai.steamstatic.com/steam/apps/{card.Game.AppId}/library_600x900.jpg";
            var localPath = await imageCacheService.GetCachedPathAsync(steamUrl, ct);

            // O download pode ter sido cancelado porque o usuário mudou de
            // página, filtro ou busca. Nesse caso não marca a capa como ausente.
            if (ct.IsCancellationRequested)
                return;

            if (string.IsNullOrEmpty(localPath))
            {
                var sgdbKey = settingsService.Current.SteamGridDbApiKey;
                if (!string.IsNullOrEmpty(sgdbKey))
                    localPath = await FetchSteamGridDbCoverAsync(card.Game.AppId, sgdbKey, ct);
            }

            if (ct.IsCancellationRequested)
                return;

            if (string.IsNullOrEmpty(localPath))
            {
                RunOnUi(() => card.CoverMissing = true);
                return;
            }

            var img = await Helpers.ImageLoader.LoadCoverAsync(localPath);
            RunOnUi(() =>
            {
                card.CoverPath = localPath;
                card.CoverImage = img;
            });
        }
        finally
        {
            RunOnUi(() => card.IsCoverLoading = false);
            _coverLoadGate.Release();
        }
    }

    private static readonly System.Net.Http.HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    [RelayCommand]
    private async Task ChangeGameAccountAsync(GameCardViewModel cardVm)
    {
        if (_allAccounts.Count == 0)
        {
            snackbarService.Show(
                "Nenhuma conta disponível",
                "Adicione uma conta antes de associar um jogo.",
                ControlAppearance.Caution,
                null,
                TimeSpan.FromSeconds(4));
            return;
        }

        var installationAccounts = FilterAccounts
            .Where(account => !string.IsNullOrEmpty(account.SteamId64)
                && account.Account.InstallationId == cardVm.Game.InstallationId)
            .ToList();
        if (installationAccounts.Count == 0)
        {
            snackbarService.Show(
                "Nenhuma conta nesta instalação",
                $"Adicione uma conta à instalação {cardVm.Game.InstallationName}.",
                ControlAppearance.Caution,
                null,
                TimeSpan.FromSeconds(4));
            return;
        }
        _ = LoadFilterAvatarsAsync(installationAccounts);
        var dialog = new SteamSwitcher.Views.Dialogs.PickAccountDialog(
            cardVm.Game.Name,
            installationAccounts,
            cardVm.Game.LoginStateOverride)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true || dialog.SelectedAccount is null)
            return;

        cardVm.Game.OwnerAccount = dialog.SelectedAccount;
        cardVm.Game.OwnerSteamId64 = dialog.SelectedAccount.SteamId64;
        cardVm.Game.LoginStateOverride = dialog.SelectedLoginState;
        _savedOwners[cardVm.Game.UniqueKey] = dialog.SelectedAccount.UniqueKey;
        cardVm.OnOwnerChanged();

        await PersistGameOwnerAsync(
            cardVm.Game.UniqueKey,
            dialog.SelectedAccount.UniqueKey);

        await gameService.SetGameLoginStateAsync(
            cardVm.Game.UniqueKey, dialog.SelectedLoginState);

        snackbarService.Show(
            "Conta do jogo alterada",
            $"{dialog.SelectedAccount.DisplayName} agora é a conta associada a {cardVm.Game.Name}.",
            ControlAppearance.Success,
            null,
            TimeSpan.FromSeconds(4));

        ApplyFilters();
    }

    private async Task<string?> FetchSteamGridDbCoverAsync(
    string appId, string apiKey, CancellationToken ct)
    {
        try
        {
            // Verifica se já temos a URL cacheada (evita 2 chamadas de API)
            var urlCacheKey = $"sgdb_url_{appId}";
            var cachedUrl = await imageCacheService.GetStringAsync(urlCacheKey);

            if (!string.IsNullOrEmpty(cachedUrl))
                return await imageCacheService.GetCachedPathAsync(cachedUrl, ct);

            // Busca o ID do jogo no SteamGridDB via Steam AppID
            var searchUrl = $"https://www.steamgriddb.com/api/v2/games/steam/{appId}";
            using var req = new System.Net.Http.HttpRequestMessage(
                System.Net.Http.HttpMethod.Get, searchUrl);
            req.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            var res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return null;

            var json = System.Text.Json.JsonDocument.Parse(
                await res.Content.ReadAsStringAsync(ct));

            var sgdbId = json.RootElement
                .GetProperty("data")
                .GetProperty("id")
                .GetInt32();

            // Busca grids (capas verticais) para esse ID
            var gridsUrl = $"https://www.steamgriddb.com/api/v2/grids/game/{sgdbId}" +
               "?dimensions=600x900,342x482&limit=1";
            using var req2 = new System.Net.Http.HttpRequestMessage(
                System.Net.Http.HttpMethod.Get, gridsUrl);
            req2.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            var res2 = await _http.SendAsync(req2, ct);
            if (!res2.IsSuccessStatusCode) return null;

            var json2 = System.Text.Json.JsonDocument.Parse(
                await res2.Content.ReadAsStringAsync(ct));

            var imageUrl = json2.RootElement
                .GetProperty("data")[0]
                .GetProperty("url")
                .GetString();

            if (string.IsNullOrEmpty(imageUrl)) return null;

            // Salva a URL para próximas sessões (30 dias)
            await imageCacheService.SetStringAsync(urlCacheKey, imageUrl, TimeSpan.FromDays(30));

            return await imageCacheService.GetCachedPathAsync(imageUrl, ct);
        }
        catch
        {
            return null;
        }
    }

    private static readonly string _gameOwnersPath = System.IO.Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "SteamSwitcher", "game_owners.json");

    private static readonly string _favoriteGamesPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamSwitcher", "favorite_games.json");

    private static async Task<HashSet<string>> LoadFavoriteGameIdsAsync()
    {
        return await AtomicJsonFile.ReadAsync(
            _favoriteGamesPath,
            static () => new HashSet<string>(StringComparer.Ordinal));
    }

    private static async Task PersistGameOwnerAsync(string appId, string steamId64)
    {
        await AtomicJsonFile.UpdateAsync(
            _gameOwnersPath,
            static () => new Dictionary<string, string>(),
            map => map[appId] = steamId64);
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    private void StartLibraryWatchers()
    {
        var watchedDirectories = _libraryWatchers
            .Select(watcher => watcher.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in gameService.GetLibraryManifestDirectories())
        {
            if (watchedDirectories.Contains(directory)) continue;

            try
            {
                var watcher = new FileSystemWatcher(directory, "appmanifest_*.acf")
                {
                    NotifyFilter = NotifyFilters.FileName
                        | NotifyFilters.LastWrite
                        | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };
                watcher.Created += OnLibraryManifestChanged;
                watcher.Changed += OnLibraryManifestChanged;
                watcher.Deleted += OnLibraryManifestChanged;
                watcher.Renamed += OnLibraryManifestChanged;
                _libraryWatchers.Add(watcher);
            }
            catch
            {
                // Uma biblioteca removível ou de rede pode estar indisponível.
            }
        }
    }

    private void OnLibraryManifestChanged(object sender, FileSystemEventArgs e)
    {
        var nextCts = new CancellationTokenSource();
        var previousCts = Interlocked.Exchange(
            ref _libraryRefreshDebounceCts,
            nextCts);
        previousCts?.Cancel();
        previousCts?.Dispose();
        _ = RefreshLibraryDebouncedAsync(nextCts.Token);
    }

    private async Task RefreshLibraryDebouncedAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            await _libraryRefreshGate.WaitAsync(ct);
            try
            {
                var rawGames = await gameService.GetInstalledGamesAsync(_allAccounts, ct);
                RunOnUi(() => ApplyLibrarySnapshot(rawGames));
            }
            finally
            {
                _libraryRefreshGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Outro evento de manifest reiniciou o debounce.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[GamesViewModel] Falha ao atualizar biblioteca: {ex.Message}");
        }
    }

    private void ApplyLibrarySnapshot(IReadOnlyList<SteamGame> rawGames)
    {
        var existing = _allGames.ToDictionary(
            card => card.Game.UniqueKey,
            StringComparer.Ordinal);
        var updated = new List<GameCardViewModel>(rawGames.Count);

        foreach (var game in rawGames)
        {
            if (_savedOwners.TryGetValue(game.UniqueKey, out var savedId)
                || _savedOwners.TryGetValue(game.AppId, out savedId))
            {
                var owner = _allAccounts.FirstOrDefault(account =>
                        account.UniqueKey == savedId)
                    ?? _allAccounts.FirstOrDefault(account =>
                        account.InstallationId == game.InstallationId
                        && account.SteamId64 == savedId);
                if (owner is not null)
                {
                    game.OwnerAccount = owner;
                    game.OwnerSteamId64 = savedId;
                }
            }

            if (existing.TryGetValue(game.UniqueKey, out var card))
            {
                card.ApplySnapshot(game);
                updated.Add(card);
            }
            else
            {
                updated.Add(new GameCardViewModel(
                    game,
                    _favoriteGameIds.Contains(game.UniqueKey)
                        || _favoriteGameIds.Contains(game.AppId)));
            }
        }

        _allGames = updated;
        ApplyFilters(resetPage: false);
        StartLibraryWatchers();
    }

    private static async Task<Dictionary<string, string>> LoadGameOwnersAsync()
    {
        return await AtomicJsonFile.ReadAsync(
            _gameOwnersPath,
            static () => new Dictionary<string, string>());
    }

    private void ApplyFilters(bool resetPage = true)
    {
        _filteredGames = Helpers.GameLibraryProjection.FilterAndSort(
            _allGames,
            SelectedFilterAccount?.UniqueKey,
            SearchText,
            GameSortMode);

        if (resetPage)
            CurrentPage = 1;

        if (CurrentPage > TotalPages)
            CurrentPage = TotalPages;

        UpdateVisiblePage();
        NotifyEmptyStates();
    }

    [RelayCommand]
    private void ClearGameFilters()
    {
        SearchText = string.Empty;
        SelectedFilterAccount = FilterAccounts.FirstOrDefault();
        ApplyFilters();
    }

    [RelayCommand]
    private async Task ToggleGameFavoriteAsync(GameCardViewModel card)
    {
        card.IsFavorite = !card.IsFavorite;
        if (card.IsFavorite)
            _favoriteGameIds.Add(card.Game.UniqueKey);
        else
            _favoriteGameIds.Remove(card.Game.UniqueKey);

        await AtomicJsonFile.UpdateAsync(
            _favoriteGamesPath,
            static () => new HashSet<string>(StringComparer.Ordinal),
            favorites =>
            {
                if (card.IsFavorite)
                    favorites.Add(card.Game.UniqueKey);
                else
                    favorites.Remove(card.Game.UniqueKey);
            });

        ApplyFilters(resetPage: false);
    }

    [RelayCommand]
    private async Task SortGamesAlphabeticallyAsync() =>
        await SetGameSortModeAsync(GameSortMode.Alphabetical);

    [RelayCommand]
    private async Task SortGamesByMostPlayedAsync() =>
        await SetGameSortModeAsync(GameSortMode.MostPlayed);

    [RelayCommand]
    private async Task SortGamesByLargestSizeAsync() =>
        await SetGameSortModeAsync(GameSortMode.LargestSize);

    private async Task SetGameSortModeAsync(GameSortMode mode)
    {
        if (GameSortMode == mode) return;

        settingsService.Current.GameSortMode = mode;
        OnPropertyChanged(nameof(GameSortMode));
        OnPropertyChanged(nameof(GameSortLabel));
        OnPropertyChanged(nameof(IsAlphabeticalGameSort));
        OnPropertyChanged(nameof(IsMostPlayedGameSort));
        OnPropertyChanged(nameof(IsLargestSizeGameSort));
        ApplyFilters();
        await settingsService.SaveAsync(settingsService.Current);
    }

    [RelayCommand]
    private async Task ShowGameGridViewAsync() =>
        await SetGameViewModeAsync(GameViewMode.Grid);

    [RelayCommand]
    private async Task ShowGameCompactViewAsync() =>
        await SetGameViewModeAsync(GameViewMode.Compact);

    private async Task SetGameViewModeAsync(GameViewMode mode)
    {
        if (GameViewMode == mode) return;

        settingsService.Current.GameViewMode = mode;
        OnPropertyChanged(nameof(GameViewMode));
        OnPropertyChanged(nameof(IsGameGridView));
        OnPropertyChanged(nameof(IsGameCompactView));
        OnPropertyChanged(nameof(IsGameGridContentVisible));
        OnPropertyChanged(nameof(IsGameCompactContentVisible));
        mainViewModel.RefreshGameGridDensityVisibility();
        await settingsService.SaveAsync(settingsService.Current);
    }

    private void NotifyEmptyStates()
    {
        OnPropertyChanged(nameof(HasNoInstalledGames));
        OnPropertyChanged(nameof(HasNoFilterResults));
        OnPropertyChanged(nameof(HasFilteredGames));
        OnPropertyChanged(nameof(IsGameGridContentVisible));
        OnPropertyChanged(nameof(IsGameCompactContentVisible));
    }

    private void UpdateVisiblePage()
    {
        var nextPage = Helpers.GameLibraryProjection.GetPage(
            _filteredGames,
            CurrentPage,
            GamesPerPage);
        var nextCards = nextPage.ToHashSet();

        // As imagens fora da página deixam de ser referenciadas pela VM. O cache
        // LRU mantém apenas as capas visitadas mais recentemente.
        foreach (var oldCard in Games)
        {
            if (!nextCards.Contains(oldCard))
                oldCard.CoverImage = null;
        }

        Games = new ObservableCollection<GameCardViewModel>(nextPage);

        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(CanGoPreviousPage));
        OnPropertyChanged(nameof(CanGoNextPage));
        OnPropertyChanged(nameof(PageText));

        RefreshStatusBar();

        // Proativamente dispara o cargamento de capas que faltam para esta página,
        // sem depender do trigger de visibilidade (que falha com virtualização).
        KickoffMissingCoverLoads(Games);
    }

    private void KickoffMissingCoverLoads(IEnumerable<GameCardViewModel> cards)
    {
        var pending = cards
            .Where(card => card.CoverImage is null && !card.CoverMissing)
            .ToList();

        var nextCts = new CancellationTokenSource();
        var previousCts = Interlocked.Exchange(ref _visibleCoverLoadCts, nextCts);
        previousCts?.Cancel();
        previousCts?.Dispose();

        _ = LoadVisibleCoversAsync(pending, nextCts.Token);
    }

    private async Task LoadVisibleCoversAsync(
        IReadOnlyList<GameCardViewModel> cards,
        CancellationToken ct)
    {
        try
        {
            await Helpers.BoundedWorkQueue.RunAsync(
                cards,
                workerCount: 6,
                LoadGameDataAsync,
                ct);
        }
        catch (OperationCanceledException)
        {
            // Esperado quando busca, filtro ou paginação troca a página visível.
        }
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (CanGoPreviousPage)
            CurrentPage--;
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CanGoNextPage)
            CurrentPage++;
    }

    public void RefreshStatusBar()
    {
        var total = _filteredGames.Count;
        mainViewModel.UpdateStatusBar($"{total} jogos", showLoginToggle: true);
    }

    [RelayCommand]
    private async Task LaunchGameAsync(GameCardViewModel cardVm)
    {
        var account = cardVm.Game.OwnerAccount;

        // Conta desconhecida — pede pro usuário escolher
        if (account is null)
        {
            if (_allAccounts.Count == 0)
            {
                snackbarService.Show(
                    "Nenhuma conta disponível",
                    "Adicione uma conta Steam para poder jogar.",
                    ControlAppearance.Caution,
                    null,
                    TimeSpan.FromSeconds(4));
                return;
            }

            var installationAccounts = FilterAccounts
                .Where(candidate => !string.IsNullOrEmpty(candidate.SteamId64)
                    && candidate.Account.InstallationId == cardVm.Game.InstallationId)
                .ToList();
            if (installationAccounts.Count == 0)
            {
                snackbarService.Show(
                    "Nenhuma conta nesta instalação",
                    $"Adicione uma conta à instalação {cardVm.Game.InstallationName}.",
                    ControlAppearance.Caution,
                    null,
                    TimeSpan.FromSeconds(4));
                return;
            }
            _ = LoadFilterAvatarsAsync(installationAccounts);
            var dialog = new SteamSwitcher.Views.Dialogs.PickAccountDialog(
                cardVm.Game.Name, installationAccounts, cardVm.Game.LoginStateOverride)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };

            var result = dialog.ShowDialog();

            if (result != true || dialog.SelectedAccount is null)
                return;

            // Apenas associa — não lança o jogo
            cardVm.Game.OwnerAccount = dialog.SelectedAccount;
            cardVm.Game.OwnerSteamId64 = dialog.SelectedAccount.SteamId64;
            cardVm.Game.LoginStateOverride = dialog.SelectedLoginState;
            _savedOwners[cardVm.Game.UniqueKey] = dialog.SelectedAccount.UniqueKey;
            cardVm.OnOwnerChanged();

            await Task.WhenAll(
                PersistGameOwnerAsync(
                    cardVm.Game.UniqueKey,
                    dialog.SelectedAccount.UniqueKey),
                gameService.SetGameLoginStateAsync(
                    cardVm.Game.UniqueKey,
                    dialog.SelectedLoginState));

            snackbarService.Show(
                "Conta associada",
                $"{dialog.SelectedAccount.DisplayName} associada a {cardVm.Game.Name}. Clique em Jogar para iniciar.",
                ControlAppearance.Success,
                null,
                TimeSpan.FromSeconds(4));

            return;
        }

        if (Interlocked.CompareExchange(ref _launchOperationActive, 1, 0) != 0)
        {
            snackbarService.Show(
                "Lançamento em andamento",
                "Aguarde o jogo atual terminar de ser preparado.",
                ControlAppearance.Caution,
                null,
                TimeSpan.FromSeconds(3));
            return;
        }

        cardVm.IsLaunching = true;
        IsGameLaunchInProgress = true;
        snackbarService.Show(
            "Iniciando jogo",
            $"Preparando {cardVm.Game.Name} com {account.DisplayName}...",
            ControlAppearance.Secondary,
            null,
            TimeSpan.FromSeconds(5));

        try
        {
            await gameService.LaunchGameAsync(cardVm.Game, account);

            var behavior = settingsService.Current.AfterGameLaunch;
            if (behavior == PostSwitchBehavior.MinimizeToTray)
                mainViewModel.HideWindowToTray();
            else if (behavior == PostSwitchBehavior.Close)
                System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            snackbarService.Show(
                "Erro",
                ex.Message,
                ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(5));
        }
        finally
        {
            cardVm.IsLaunching = false;
            IsGameLaunchInProgress = false;
            Volatile.Write(ref _launchOperationActive, 0);
        }
    }
}
