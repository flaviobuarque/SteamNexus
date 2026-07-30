using System.Text.Json;

namespace SteamSwitcher.Core.Services;

public class WatchdogService : IWatchdogService
{
    private static readonly string _flagPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamSwitcher", "watchdog.json");

    public void BeginSwitch(string targetSteamId64)
    {
        var data = new WatchdogData
        {
            InProgress = true,
            TargetSteamId64 = targetSteamId64,
            StartedAt = DateTime.UtcNow
        };
        File.WriteAllText(_flagPath,
            JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void EndSwitch()
    {
        if (File.Exists(_flagPath))
            File.Delete(_flagPath);
    }

    public bool HasInterruptedSwitch(out string? interruptedSteamId64)
    {
        interruptedSteamId64 = null;
        if (!File.Exists(_flagPath)) return false;

        try
        {
            var json = File.ReadAllText(_flagPath);
            var data = JsonSerializer.Deserialize<WatchdogData>(json);
            if (data is { InProgress: true })
            {
                interruptedSteamId64 = data.TargetSteamId64;
                return true;
            }
        }
        catch { /* flag corrompida */ }

        return false;
    }

    public void ClearInterruptedSwitch() => EndSwitch();

    private class WatchdogData
    {
        public bool InProgress { get; set; }
        public string? TargetSteamId64 { get; set; }
        public DateTime StartedAt { get; set; }
    }
}