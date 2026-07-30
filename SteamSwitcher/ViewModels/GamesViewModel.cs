using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Input;
using SteamSwitcher.Core;
using SteamSwitcher.Core.Models;
using SteamSwitcher.Core.Services;
using System.Collections.ObjectModel;
using System.IO;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SteamSwitcher.ViewModels;

public partial class GamesViewModel(
    ISteamGameService gameService,
    ISteamAccountService accountService,
    IImageCacheService imageCacheService,
    ISnackbarService snackbarService,
    IAppSettingsService settingsService,
    IGameProcessService gameProcessService,
    MainViewModel mainViewModel) : ObservableObject
{
    private IReadOnlyList<GameCardViewModel> _allGames = [];
    private IReadOnlyList<GameCardViewModel> _filteredGames = [];
    private const int GamesPerPage = 60;
    private IReadOnlyList<SteamAccount> _allAccounts = [];
    private bool _processHandlerRegistered;
    private readonly SemaphoreSlim _coverLoadGate = new(6, 6);

    [ObservableProperty] private ObservableCollection<GameCardViewModel> _games = [];
    [ObservableProperty] private ObservableCollection<SteamAccount> _filterAccounts = [];
    [ObservableProperty] private SteamAccount? _selectedFilterAccount;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isLoading;

    [ObservableProperty] private ObservableCollection<SteamAccount> _visibleFilterAccounts = [];
    [ObservableProperty] private string _filterAccountSearchText = string.Empty;
    [ObservableProperty] private bool _isAccountFilterOpen;

    [ObservableProperty] private int _currentPage = 1;

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnSelectedFilterAccountChanged(SteamAccount? value) => ApplyFilters();
    public bool IsEmpty => !IsLoading && Games.Count == 0;
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));
    partial void OnGamesChanged(ObservableCollection<GameCardViewModel> value) => OnPropertyChanged(nameof(IsEmpty));

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
                        card.HeroCoverPath = string.Empty;
                        card.ResetLoadState();
                    }

                    // Re-kickoff para a página visível (e para os jogos visíveis)
                    // já que o reset acima só dispara LoadRequested para cards
                    // marcados como in-viewport (irrelevante com virtualização).
                    KickoffMissingCoverLoads(Games);
                });
            });

        IsLoading = true;
        try
        {
            var accounts = DebugDemoData.TryCreateAccountsFromArgs()
                ?? await accountService.GetAccountsAsync(ct);
            _allAccounts = accounts;

            // Monta lista de filtros
            FilterAccounts = new ObservableCollection<SteamAccount>(
                accounts.Prepend(new SteamAccount
                {
                    SteamId64 = string.Empty,
                    AccountName = string.Empty,
                    PersonaName = "Todas as contas"
                }));
            SelectedFilterAccount = FilterAccounts.First();

            VisibleFilterAccounts = new ObservableCollection<SteamAccount>(FilterAccounts);

            // Carrega jogos
            var rawGames = await gameService.GetInstalledGamesAsync(accounts, ct);
            var cards = rawGames.Select(g => new GameCardViewModel(g)).ToList();
            _allGames = cards;

            var savedOwners = await LoadGameOwnersAsync();
            if (savedOwners.Count > 0)
            {
                foreach (var card in cards)
                {
                    if (savedOwners.TryGetValue(card.Game.AppId, out var savedId))
                    {
                        var owner = _allAccounts.FirstOrDefault(a => a.SteamId64 == savedId);
                        if (owner is not null)
                        {
                            card.Game.OwnerAccount = owner;
                            card.Game.OwnerSteamId64 = savedId;
                            card.OnOwnerChanged();
                        }
                    }
                }
            }

            foreach (var card in cards)
            {
                card.LoadRequested -= OnCardLoadRequested;
                card.LoadRequested += OnCardLoadRequested;
            }

            // Fase 1: resolve cache local imediatamente (sem rede)
            var cacheDir = imageCacheService.GetCacheDirectory();

            var cachedCovers = await Task.Run(() =>
            {
                return cards
                    .Select(card =>
                    {
                        var steamUrl =
                            $"https://cdn.akamai.steamstatic.com/steam/apps/{card.Game.AppId}/library_600x900.jpg";

                        var hash = Convert.ToHexString(
                            System.Security.Cryptography.SHA256.HashData(
                                System.Text.Encoding.UTF8.GetBytes(steamUrl)))[..16];

                        var path = Path.Combine(cacheDir, hash + ".jpg");

                        return new
                        {
                            Card = card,
                            Path = File.Exists(path) && new FileInfo(path).Length > 1024
                                ? path
                                : null
                        };
                    })
                    .Where(x => x.Path is not null)
                    .ToList();
            }, ct);

            foreach (var cached in cachedCovers)
            {
                cached.Card.CoverPath = cached.Path!;
                var img = await Helpers.ImageLoader.LoadCoverAsync(cached.Path);
                cached.Card.CoverImage = img;
            }

            ApplyFilters();

            var tracked = cards
                .Where(c => !string.IsNullOrEmpty(c.Game.InstallFullPath))
                .Select(c => $"{c.Game.AppId}|{c.Game.InstallFullPath}");

            gameProcessService.SetTrackedGames(tracked);
            if (!_processHandlerRegistered)
            {
                gameProcessService.GameStateChanged += OnGameStateChanged;
                _processHandlerRegistered = true;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnFilterAccountSearchTextChanged(string value) =>
    ApplyAccountFilterSearch();

    [RelayCommand]
    private void SelectGameFilterAccount(SteamAccount account)
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

        VisibleFilterAccounts = new ObservableCollection<SteamAccount>(filtered);
    }

    private async void OnCardLoadRequested(object? sender, EventArgs e)
    {
        if (sender is not GameCardViewModel c) return;
        c.MarkLoadRequested();
        await LoadGameDataAsync(c, CancellationToken.None);
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

    private async Task LoadGameDataAsync(GameCardViewModel card, CancellationToken ct)
    {
        // Limita concorrência: múltiplos cards sem capa não disparam todas
        // as HTTPs de uma vez.
        await _coverLoadGate.WaitAsync(ct);

        try
        {
            // Busca capa
            var steamUrl = $"https://cdn.akamai.steamstatic.com/steam/apps/{card.Game.AppId}/library_600x900.jpg";
            var localPath = await imageCacheService.GetCachedPathAsync(steamUrl, ct);

            if (string.IsNullOrEmpty(localPath))
            {
                var sgdbKey = settingsService.Current.SteamGridDbApiKey;
                if (!string.IsNullOrEmpty(sgdbKey))
                    localPath = await FetchSteamGridDbCoverAsync(card.Game.AppId, sgdbKey, ct);
            }

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

        var dialog = new SteamSwitcher.Views.Dialogs.PickAccountDialog(
            cardVm.Game.Name,
            _allAccounts,
            cardVm.Game.LoginStateOverride)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true || dialog.SelectedAccount is null)
            return;

        cardVm.Game.OwnerAccount = dialog.SelectedAccount;
        cardVm.Game.OwnerSteamId64 = dialog.SelectedAccount.SteamId64;
        cardVm.Game.LoginStateOverride = dialog.SelectedLoginState;
        cardVm.OnOwnerChanged();

        await PersistGameOwnerAsync(
            cardVm.Game.AppId,
            dialog.SelectedAccount.SteamId64);

        await gameService.SetGameLoginStateAsync(
            cardVm.Game.AppId, dialog.SelectedLoginState);

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

    private static async Task PersistGameOwnerAsync(string appId, string steamId64)
    {
        Dictionary<string, string> map;
        try
        {
            if (File.Exists(_gameOwnersPath))
            {
                var raw = await File.ReadAllTextAsync(_gameOwnersPath);
                map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(raw) ?? [];
            }
            else map = [];
        }
        catch { map = []; }

        map[appId] = steamId64;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_gameOwnersPath)!);
        await File.WriteAllTextAsync(_gameOwnersPath,
            System.Text.Json.JsonSerializer.Serialize(map,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    private static async Task<Dictionary<string, string>> LoadGameOwnersAsync()
    {
        try
        {
            if (!File.Exists(_gameOwnersPath)) return [];
            var raw = await File.ReadAllTextAsync(_gameOwnersPath);
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(raw) ?? [];
        }
        catch { return []; }
    }

    private void ApplyFilters(bool resetPage = true)
    {
        var filtered = _allGames.AsEnumerable();

        if (!string.IsNullOrEmpty(SelectedFilterAccount?.SteamId64))
        {
            filtered = filtered.Where(game =>
                game.Game.OwnerSteamId64 == SelectedFilterAccount.SteamId64);
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = filtered.Where(game =>
                game.Game.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        _filteredGames = filtered.ToList();

        if (resetPage)
            CurrentPage = 1;

        if (CurrentPage > TotalPages)
            CurrentPage = TotalPages;

        UpdateVisiblePage();
    }

    private void UpdateVisiblePage()
    {
        var skippedItems = (CurrentPage - 1) * GamesPerPage;

        Games = new ObservableCollection<GameCardViewModel>(
            _filteredGames
                .Skip(skippedItems)
                .Take(GamesPerPage));

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
        foreach (var card in cards)
        {
            if (string.IsNullOrEmpty(card.CoverPath)
                && !card.CoverMissing
                && !card.HasLoadBeenRequested)
            {
                card.MarkLoadRequested();
                _ = LoadGameDataAsync(card, CancellationToken.None);
            }
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
        var running = _allGames.Count(game => game.IsRunning);
        var total = _filteredGames.Count;

        var left = running > 0
            ? $"{total} jogos · {running} em execução"
            : $"{total} jogos";

        mainViewModel.UpdateStatusBar(left, showLoginToggle: true);
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

            var dialog = new SteamSwitcher.Views.Dialogs.PickAccountDialog(
                cardVm.Game.Name, _allAccounts, cardVm.Game.LoginStateOverride)
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
            cardVm.OnOwnerChanged();

            _ = PersistGameOwnerAsync(cardVm.Game.AppId, dialog.SelectedAccount.SteamId64);
            _ = gameService.SetGameLoginStateAsync(cardVm.Game.AppId, dialog.SelectedLoginState);

            snackbarService.Show(
                "Conta associada",
                $"{dialog.SelectedAccount.DisplayName} associada a {cardVm.Game.Name}. Clique em Jogar para iniciar.",
                ControlAppearance.Success,
                null,
                TimeSpan.FromSeconds(4));

            return;
        }

        cardVm.IsLaunching = true;
        snackbarService.Show(
            "Iniciando jogo",
            $"Trocando para {account.DisplayName} e abrindo {cardVm.Game.Name}...",
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
        }
    }
    private void OnGameStateChanged(object? sender, GameStateChangedEventArgs e)
    {
        var card = _allGames.FirstOrDefault(c => c.Game.AppId == e.AppId);
        if (card is null) return;

        // Atualiza na UI thread
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            card.IsRunning = e.IsRunning;
            RefreshStatusBar();
        });
    }

    /// <summary>Chamado pela GamesPage quando a página entra/sai de foco.</summary>
    public void SetPollingActive(bool active)
    {
        if (active) gameProcessService.Resume();
        else gameProcessService.Pause();
    }
}