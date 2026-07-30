using System.Text.Json;
using SteamSwitcher.Core.Models;

namespace SteamSwitcher.Core.Services;

public class AccountOverrideService : IAccountOverrideService
{
    private static readonly string _overridesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamSwitcher", "overrides.json");

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private Dictionary<string, AccountOverride> _overrides = [];

    public AccountOverrideService() => _ = LoadAsync();

    private async Task LoadAsync()
    {
        if (!File.Exists(_overridesPath)) return;
        try
        {
            var json = await File.ReadAllTextAsync(_overridesPath);
            _overrides = JsonSerializer.Deserialize<Dictionary<string, AccountOverride>>(
                json, _jsonOptions) ?? [];
        }
        catch { _overrides = []; }
    }

    public Task<AccountOverride?> GetOverrideAsync(string steamId64)
    {
        _overrides.TryGetValue(steamId64, out var o);
        return Task.FromResult(o);
    }

    public async Task SaveOverrideAsync(string steamId64, AccountOverride data)
    {
        _overrides[steamId64] = data;
        await PersistAsync();
    }

    public async Task RemoveOverrideAsync(string steamId64)
    {
        _overrides.Remove(steamId64);
        await PersistAsync();
    }

    private async Task PersistAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_overridesPath)!);
        var json = JsonSerializer.Serialize(_overrides, _jsonOptions);
        await File.WriteAllTextAsync(_overridesPath, json);
    }
}