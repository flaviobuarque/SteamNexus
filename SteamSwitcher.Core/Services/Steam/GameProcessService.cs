using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace SteamSwitcher.Core.Services;

public sealed class GameProcessService(
    ISteamLocatorService steamLocator,
    ILogger<GameProcessService> logger) : IGameProcessService, IAsyncDisposable
{
    public event EventHandler<GameStateChangedEventArgs>? GameStateChanged;

    private readonly object _sync = new();
    // appId -> installPath (lowercased), vindo de SetTrackedGames.
    private Dictionary<string, string> _tracked = [];
    // appId -> running state
    private Dictionary<string, bool> _lastState = [];

    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private bool _paused;

    public void SetTrackedGames(IEnumerable<string> installFullPaths)
    {
        var tracked = installFullPaths
            .Select(entry => entry.Split('|', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(
                parts => parts[0],
                parts => parts[1].ToLowerInvariant());

        lock (_sync)
        {
            _tracked = tracked;
            _lastState = tracked.Keys.ToDictionary(id => id, _ => false);
        }

        if (_loopTask is null)
            Start();
    }

    public bool IsRunning(string appId)
    {
        lock (_sync)
            return _lastState.GetValueOrDefault(appId);
    }

    public void Resume() => _paused = false;
    public void Pause() => _paused = true;

    private void Start()
    {
        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        _loopTask = RunAsync(_cts.Token);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (await _timer!.WaitForNextTickAsync(ct))
            {
                if (_paused) continue;
                await PollAsync();
            }
        }
        catch (OperationCanceledException) { }
    }

    private Task PollAsync()
    {
        try
        {
            Dictionary<string, string> tracked;
            lock (_sync)
            {
                if (_tracked.Count == 0) return Task.CompletedTask;
                tracked = new Dictionary<string, string>(_tracked);
            }

            // Nao enumeramos todos os processos do SO (padrao suspeito a AVs).
            // Em vez disso, mantemos um cache de caminhos de exe por jogo verificado
            // sob demanda (lazy): quando o jogo e detectado pela primeira vez,
            // varremos o diretorio de instalacao do proprio jogo (.exe) e procuramos
            // apenas o processo correspondente (Process.GetProcessesByName sem
            // acionar MainModule de todos os processos).
            foreach (var (appId, installPath) in tracked)
            {
                var isRunning = CheckIfRunning(installPath);

                bool was;
                lock (_sync)
                {
                    was = _lastState.GetValueOrDefault(appId);
                    if (isRunning == was) continue;
                    _lastState[appId] = isRunning;
                }

                logger.LogDebug("GameStateChanged: {AppId} -> {State}", appId, isRunning);
                GameStateChanged?.Invoke(this, new GameStateChangedEventArgs(appId, isRunning));
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Erro no poll de processos");
        }

        return Task.CompletedTask;
    }

    private static readonly Dictionary<string, string[]> _exeCache = new(StringComparer.OrdinalIgnoreCase);

    // Descobre os nomes de executaveis (.exe) dentro da pasta do jogo (uma vez) e
    // checa se algum processo com esse nome esta rodando. Nao escaneia todos os
    // processos do SO nem le MainModule de nada — apenas GetProcessesByName(nome),
    // que e barato e nao e tipicamente marcado por heuristica de antivirrus.
    private static bool CheckIfRunning(string installPath)
    {
        if (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath)) return false;

        string[] exeNames;
        lock (_exeCache)
        {
            if (!_exeCache.TryGetValue(installPath, out exeNames))
            {
                try
                {
                    exeNames = Directory.EnumerateFiles(installPath, "*.exe", SearchOption.TopDirectoryOnly)
                        .Select(f => Path.GetFileNameWithoutExtension(f))
                        .ToArray();
                }
                catch
                {
                    exeNames = [];
                }
                _exeCache[installPath] = exeNames;
            }
        }

        foreach (var exe in exeNames)
        {
            if (Process.GetProcessesByName(exe).Length > 0)
                return true;
        }

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
        }
        _timer?.Dispose();
        if (_loopTask is not null)
            await _loopTask.ConfigureAwait(false);
    }
}