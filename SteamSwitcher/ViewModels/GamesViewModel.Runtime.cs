using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamSwitcher.Core.Services;
using SteamSwitcher.Core.Models;
using SteamSwitcher.Views.Dialogs;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace SteamSwitcher.ViewModels;

public partial class GamesViewModel
{
    private readonly CancellationTokenSource _runtimeCancellation = new();
    private bool _runtimeMonitorStarted;
    private readonly SemaphoreSlim _runtimeRefreshGate = new(1, 1);
    private IReadOnlyDictionary<string, GameRuntimeSnapshot> _runtimeSnapshots = new Dictionary<string, GameRuntimeSnapshot>();
    private sealed class UninstallRequest(SteamGame game)
    {
        public SteamGame Game { get; } = game;
        public DateTime RequestedUtc { get; } = DateTime.UtcNow;
        public DateTime? ManifestMissingSince { get; set; }
        public bool Warned { get; set; }
    }
    private readonly Dictionary<string, UninstallRequest> _uninstallRequests = new();
    private Window? _gameActionOwner;
    public ObservableCollection<GameCardViewModel> RunningGames { get; } = [];
    public bool HasRunningGames => RunningGames.Count > 0;
    public bool HasPendingUninstall => _uninstallRequests.Count > 0;
    [ObservableProperty] private string _gameOperationMessage = string.Empty;

    private async Task MonitorGamesAsync(CancellationToken ct)
    {
        var idleCycles = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RefreshRuntimeAsync();
                var running = _runtimeSnapshots.Values.Any(state => state.IsRunning || state.IsUncertain);
                var pending = IsGameLaunchInProgress || HasPendingUninstall || mainViewModel.IsSwitchingAccount;
                idleCycles = running || pending ? 0 : Math.Min(idleCycles + 1, 3);
                var delay = Core.Helpers.RuntimePollingPolicy.DelayMilliseconds(
                    mainViewModel.IsWindowVisible, running, pending, idleCycles);
                Core.Helpers.RuntimeMonitorMetrics.Shared.SetDelay(delay);
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                GameOperationMessage = $"Não foi possível atualizar os jogos: {ex.Message}";
                try { await Task.Delay(5000, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task RefreshRuntimeAsync()
    {
        await _runtimeRefreshGate.WaitAsync(_runtimeCancellation.Token);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var succeeded = false;
        try
        {
            var snapshots = await runtimeService.InspectAsync(_allGames.Select(card => card.Game).ToArray(), _runtimeCancellation.Token);
            _runtimeSnapshots = snapshots;
            mainViewModel.IsGameRunning = snapshots.Values.Any(state => state.IsRunning || state.IsUncertain);
            SteamAccount? activeAccount = null;
            if (snapshots.Values.Any(state => state.IsRunning))
            {
                try
                {
                    activeAccount = await accountService.GetActiveAccountAsync(_runtimeCancellation.Token);
                }
                // Steam can rewrite its session file while being polled. Keep the
                // last known account and still refresh the game processes.
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            var orderChanged = false;
            var gameStarted = false;
            foreach (var card in _allGames)
            {
                if (!snapshots.TryGetValue(card.Game.UniqueKey, out var state)) continue;
                orderChanged |= card.IsRunning != state.IsRunning;
                gameStarted |= !card.IsRunning && state.IsRunning;
                if (state.Processes.Count > 0)
                {
                    var previousLastPlayed = card.Game.LastPlayedTimestamp;
                    card.Game.LastPlayedTimestamp = Math.Max(card.Game.LastPlayedTimestamp,
                        new DateTimeOffset(state.Processes.Min(process => process.StartedUtc)).ToUnixTimeSeconds());
                    orderChanged |= GameSortMode == GameSortMode.RecentlyPlayed && previousLastPlayed != card.Game.LastPlayedTimestamp;
                    var executable = state.Processes[0].Executable;
                    if (card.RuntimeIconPath != executable)
                    {
                        card.RuntimeIconPath = executable;
                        card.TrayIconImage = await Helpers.GameExecutableIconLoader.LoadAsync(executable);
                    }
                }
                card.IsRunning = state.IsRunning;
                card.RuntimeUncertain = state.IsUncertain;
                if (!state.IsRunning) card.RuntimeAccountName = "Conta não identificada";
                else if (activeAccount is not null && card.Game.InstallationId == activeAccount.InstallationId)
                {
                    var displayAccount = _allAccounts.FirstOrDefault(account => account.UniqueKey == activeAccount.UniqueKey)
                        ?? activeAccount;
                    card.RuntimeAccountName = string.IsNullOrWhiteSpace(displayAccount.DisplayName)
                        ? activeAccount.AccountName
                        : displayAccount.DisplayName;
                }
            }
            var running = _allGames.Where(card => card.IsRunning)
                .DistinctBy(card => card.Game.InstallFullPath, StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var old in RunningGames.Except(running).ToArray()) RunningGames.Remove(old);
            foreach (var card in running.Except(RunningGames)) RunningGames.Add(card);
            OnPropertyChanged(nameof(HasRunningGames));
            if (orderChanged) ApplyFilters(resetPage: gameStarted);
            foreach (var request in _uninstallRequests.ToArray())
            {
                var library = Path.Combine(request.Value.Game.LibraryPath, "steamapps");
                var manifest = Path.Combine(library, $"appmanifest_{request.Value.Game.AppId}.acf");
                if (Directory.Exists(library) && !File.Exists(manifest))
                {
                    request.Value.ManifestMissingSince ??= DateTime.UtcNow;
                    if (DateTime.UtcNow - request.Value.ManifestMissingSince.Value > TimeSpan.FromSeconds(2))
                    {
                        await _libraryRefreshGate.WaitAsync(_runtimeCancellation.Token);
                        try
                        {
                            var games = await gameService.GetInstalledGamesAsync(_allAccounts, _runtimeCancellation.Token);
                            ApplyLibrarySnapshot(games);
                        }
                        finally { _libraryRefreshGate.Release(); }
                        if (!File.Exists(manifest) && Directory.Exists(library))
                        {
                            GameOperationMessage = $"{request.Value.Game.Name}: instalação removida da biblioteca Steam.";
                            _uninstallRequests.Remove(request.Key);
                        }
                    }
                }
                else request.Value.ManifestMissingSince = null;
                if (!request.Value.Warned && DateTime.UtcNow - request.Value.RequestedUtc > TimeSpan.FromMinutes(2))
                {
                    request.Value.Warned = true;
                    GameOperationMessage = "A desinstalação ainda não foi confirmada. Verifique a Steam. Se você cancelou, use ‘Já cancelei na Steam’ para liberar as ações.";
                }
            }
            OnPropertyChanged(nameof(HasPendingUninstall));
            succeeded = true;
        }
        finally
        {
            Core.Helpers.RuntimeMonitorMetrics.Shared.Record(stopwatch.Elapsed.TotalMilliseconds, succeeded);
            _runtimeRefreshGate.Release();
        }
    }

    private bool ConfirmGameAction(string title, string message, string action)
    {
        var dialog = new ConfirmDialog(title, message, action, kind: ConfirmDialog.DialogKind.Danger)
        {
            Owner = _gameActionOwner ?? Application.Current.MainWindow
        };
        dialog.CancelButton.IsDefault = true;
        dialog.CancelButton.IsCancel = true;
        dialog.Loaded += (_, _) => dialog.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First));
        dialog.ShowDialog();
        return dialog.Confirmed;
    }

    private bool TryBeginGameOperation()
    {
        if (_uninstallRequests.Count > 0)
        {
            GameOperationMessage = "Aguarde a desinstalação. Se você cancelou na Steam, use ‘Já cancelei na Steam’.";
            return false;
        }
        if (Interlocked.CompareExchange(ref _launchOperationActive, 1, 0) != 0) return false;
        IsGameLaunchInProgress = true;
        return true;
    }

    private void EndGameOperation()
    {
        IsGameLaunchInProgress = false;
        Volatile.Write(ref _launchOperationActive, 0);
    }

    [RelayCommand]
    private void ViewRunningGame(GameCardViewModel card)
    {
        try
        {
            if (_runtimeSnapshots.TryGetValue(card.Game.UniqueKey, out var state)) runtimeService.BringToFront(state);
        }
        catch (Exception ex) { GameOperationMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task StopGameAsync(GameCardViewModel card)
    {
        if (!TryBeginGameOperation()) return;
        try
        {
            if (!ConfirmGameAction("Encerrar jogo", $"Deseja encerrar {card.Game.Name}?\n\nProgresso não salvo pode ser perdido.", "Encerrar jogo")) return;
            await StopGameCoreAsync(card);
        }
        catch (Exception ex) { GameOperationMessage = ex.Message; }
        finally { EndGameOperation(); }
    }

    private async Task<bool> StopGameCoreAsync(GameCardViewModel card)
    {
        await RefreshRuntimeAsync();
        if (!_runtimeSnapshots.TryGetValue(card.Game.UniqueKey, out var state)) return false;
        if (!state.IsRunning && !state.IsUncertain) return true;
        card.IsStopping = true;
        GameOperationMessage = $"Encerrando {card.Game.Name}…";
        try
        {
            var stopped = await runtimeService.StopAsync(card.Game, state, false, _runtimeCancellation.Token);
            if (!stopped)
            {
                await RefreshRuntimeAsync();
                if (!_runtimeSnapshots.TryGetValue(card.Game.UniqueKey, out var remaining)
                    || remaining.IsUncertain || remaining.Processes.Count == 0)
                {
                    GameOperationMessage = "A Steam ainda não confirmou o encerramento, mas não há um processo seguro para encerrar à força. Verifique a Steam e tente novamente.";
                    return false;
                }
            }
            if (!stopped && ConfirmGameAction("O jogo ainda está aberto", $"{card.Game.Name} não encerrou. Deseja forçar o encerramento?\n\nIsso pode causar perda de progresso não salvo. Apenas os processos identificados serão encerrados.", "Forçar encerramento"))
                stopped = await runtimeService.StopAsync(card.Game, state, true, _runtimeCancellation.Token);
            GameOperationMessage = stopped ? string.Empty : "O jogo continua aberto. Feche-o manualmente para continuar.";
            return stopped;
        }
        finally
        {
            card.IsStopping = false;
            await RefreshRuntimeAsync();
        }
    }

    [RelayCommand]
    private async Task UninstallGameAsync(GameCardViewModel card)
    {
        if (!TryBeginGameOperation()) return;
        try
        {
            if (!await RequireStoppedGameAsync(card)) return;
            if (!Directory.Exists(card.Game.InstallFullPath)) throw new InvalidOperationException("A pasta do jogo não está disponível. Verifique o disco antes de continuar.");
            runtimeService.ValidateUninstallTarget(card.Game);
            if (!ConfirmGameAction("Desinstalar jogo", $"{card.Game.Name}\n{card.InstallSize}\n{card.InstallFullPath}\n\nEsta instalação será removida para todas as contas que usam os mesmos arquivos. A confirmação final será feita na Steam. Não apague saves ou mods sem um backup. O jogo precisa estar parado.", "Continuar na Steam")) return;
            if (!await RequireStoppedGameAsync(card)) return;
            runtimeService.RequestUninstall(card.Game);
            card.IsUninstalling = true;
            _uninstallRequests[card.Game.UniqueKey] = new(card.Game);
            OnPropertyChanged(nameof(HasPendingUninstall));
            GameOperationMessage = $"Confirme a desinstalação de {card.Game.Name} na Steam. A biblioteca será atualizada automaticamente.";
        }
        catch (Exception ex) { GameOperationMessage = ex.Message; }
        finally { EndGameOperation(); }
    }

    public void Dispose()
    {
        _runtimeCancellation.Cancel();
        foreach (var watcher in _libraryWatchers) watcher.Dispose();
        _libraryRefreshDebounce.Dispose();
        _visibleCoverLoad.Dispose();
        _gameSearchDebounce.Dispose();
        _accountSearchDebounce.Dispose();
    }

    private async Task<bool> RequireStoppedGameAsync(GameCardViewModel card)
    {
        await RefreshRuntimeAsync();
        if (_runtimeSnapshots.TryGetValue(card.Game.UniqueKey, out var state)
            && !state.IsRunning && !state.IsUncertain && !card.IsBusy) return true;
        snackbarService.Show("Pare o jogo primeiro", "O jogo precisa estar parado para alterar a conta ou desinstalar.",
            Wpf.Ui.Controls.ControlAppearance.Caution, null, TimeSpan.FromSeconds(5));
        return false;
    }

    [RelayCommand]
    private void AcknowledgeUninstallCancellation()
    {
        if (IsGameLaunchInProgress || !ConfirmGameAction("Cancelar acompanhamento", "Confirme somente se você já cancelou a desinstalação na Steam. Esta ação não cancela uma operação que a Steam ainda esteja executando.", "Já cancelei na Steam")) return;
        foreach (var card in _allGames) card.IsUninstalling = false;
        _uninstallRequests.Clear();
        OnPropertyChanged(nameof(HasPendingUninstall));
        GameOperationMessage = "Acompanhamento cancelado. Nenhum arquivo foi removido pelo SteamNexus.";
    }
}
