namespace SteamSwitcher.Core.Services;

public interface IModMonitorService
{
    IReadOnlyList<DetectedMod> DetectedMods { get; }
    event EventHandler<DetectedMod>? ModDetected;
    event EventHandler<DetectedMod>? ModRemoved;
    Task ScanAsync(string steamPath, CancellationToken ct = default);
    void StartWatching(string steamPath);
    void StopWatching();
}

public class DetectedMod
{
    public required string Name { get; init; }
    public string? Version { get; set; }
    public required ModType Type { get; init; }
    public required string Path { get; init; }
    public bool IsSuspicious { get; set; }
}

public enum ModType { Skin, Plugin, Patcher, Millennium, Unknown }