using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SteamSwitcher.Core.Services;

public class AccountOverrideService : IAccountOverrideService
{
    private readonly ILogger<AccountOverrideService> _logger;
    private static readonly string DefaultOverridesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamSwitcher", "overrides.json");

    private readonly string _overridesPath;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ConcurrentDictionary<string, AccountOverride> _overrides = new(StringComparer.Ordinal);
    private readonly Task _initialLoadTask;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public AccountOverrideService(ILogger<AccountOverrideService> logger, string? overridesPath = null)
    {
        _logger = logger;
        _overridesPath = overridesPath ?? DefaultOverridesPath;
        _initialLoadTask = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (!File.Exists(_overridesPath)) return;
        try
        {
            var json = await File.ReadAllTextAsync(_overridesPath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, AccountOverride>>(
                json, _jsonOptions);
            if (loaded is not null)
            {
                _overrides.Clear();
                foreach (var (k, v) in loaded)
                    _overrides[k] = v;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erro ao carregar overrides de contas");
        }
    }

    public async Task<AccountOverride?> GetOverrideAsync(string steamId64)
    {
        await _initialLoadTask;
        return _overrides.TryGetValue(steamId64, out var o) ? o : null;
    }

    public async Task SaveOverrideAsync(string steamId64, AccountOverride data)
    {
        await _initialLoadTask;
        await _writeGate.WaitAsync();
        try
        {
            _overrides[steamId64] = data;
            await PersistAsync();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task RemoveOverrideAsync(string steamId64)
    {
        await _initialLoadTask;
        await _writeGate.WaitAsync();
        try
        {
            if (_overrides.TryRemove(steamId64, out _))
                await PersistAsync();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task PersistAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_overridesPath)!);
        var json = JsonSerializer.Serialize(_overrides, _jsonOptions);
        var tempPath = _overridesPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, _overridesPath, overwrite: true);
    }
}
