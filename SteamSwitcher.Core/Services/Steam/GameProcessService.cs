using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace SteamSwitcher.Core.Services;

public sealed class GameProcessService(ILogger<GameProcessService> logger) : IGameProcessService, IAsyncDisposable
{
    public event EventHandler<GameStateChangedEventArgs>? GameStateChanged;

    private readonly object _sync = new();
    private Dictionary<string, string> _tracked = [];
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

            var processes = Process.GetProcesses();
            var runningPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var proc in processes)
            {
                try
                {
                    var path = proc.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(path))
                        runningPaths.Add(path.ToLowerInvariant());
                }
                catch { }
                finally { proc.Dispose(); }
            }

            foreach (var (appId, installPath) in tracked)
            {
                var isRunning = runningPaths.Any(p => p.StartsWith(installPath));
                bool was;
                lock (_sync)
                {
                    was = _lastState.GetValueOrDefault(appId);
                    if (isRunning == was) continue;
                    _lastState[appId] = isRunning;
                }

                logger.LogDebug("GameStateChanged: {AppId} → {State}", appId, isRunning);
                GameStateChanged?.Invoke(this, new GameStateChangedEventArgs(appId, isRunning));
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Erro no poll de processos");
        }

        return Task.CompletedTask;
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
