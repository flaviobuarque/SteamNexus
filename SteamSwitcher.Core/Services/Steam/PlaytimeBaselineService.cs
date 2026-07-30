using System.Text.Json;

namespace SteamSwitcher.Core.Services;

public class PlaytimeBaselineService : IPlaytimeBaselineService
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamSwitcher", "playtime_baseline.json");

    // steamId64 -> appId -> (baseline, date)
    private Dictionary<string, Dictionary<string, BaselineEntry>> _data = [];

    public PlaytimeBaselineService() => _ = LoadAsync();

    public async Task<int> GetBaselineAsync(string steamId64, string appId)
    {
        if (_data.TryGetValue(steamId64, out var apps) &&
            apps.TryGetValue(appId, out var entry))
            return entry.Baseline;
        return 0;
    }

    public async Task SetBaselineAsync(string steamId64, string appId, int playtimeMinutes)
    {
        if (!_data.ContainsKey(steamId64))
            _data[steamId64] = [];

        // Só seta se não existir ainda (primeira detecção)
        if (!_data[steamId64].ContainsKey(appId))
        {
            _data[steamId64][appId] = new BaselineEntry
            {
                Baseline = playtimeMinutes,
                Since = DateTime.UtcNow
            };
            await SaveAsync();
        }
    }

    public int CalculateDelta(int currentMinutes, int baselineMinutes) =>
        Math.Max(0, currentMinutes - baselineMinutes);

    public async Task<DateTime?> GetBaselineDateAsync(string steamId64, string appId)
    {
        if (_data.TryGetValue(steamId64, out var apps) &&
            apps.TryGetValue(appId, out var entry))
            return entry.Since;
        return null;
    }

    private async Task LoadAsync()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var json = await File.ReadAllTextAsync(_path);
            _data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, BaselineEntry>>>(json)
                    ?? [];
        }
        catch { _data = []; }
    }

    private async Task SaveAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_path, json);
    }

    private class BaselineEntry
    {
        public int Baseline { get; set; }
        public DateTime Since { get; set; }
    }
}