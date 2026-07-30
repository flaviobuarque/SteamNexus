namespace SteamSwitcher.Core.Services;

public interface IGameProcessService
{
    event EventHandler<GameStateChangedEventArgs>? GameStateChanged;
    void SetTrackedGames(IEnumerable<string> installFullPaths);
    bool IsRunning(string appId);
    void Resume();
    void Pause();
}

public sealed class GameStateChangedEventArgs(string appId, bool isRunning) : EventArgs
{
    public string AppId { get; } = appId;
    public bool IsRunning { get; } = isRunning;
}