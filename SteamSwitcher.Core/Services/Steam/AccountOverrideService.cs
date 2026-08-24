using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace SteamSwitcher.Core.Services;

public class AccountOverrideService : IAccountOverrideService
{
    private readonly ILogger<AccountOverrideService> _logger;
    private static readonly string _overridesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamSwitcher", "overrides.json");

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private Dictionary<string, AccountOverride> _overrides = [];
    private readonly Task _initialLoadTask;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public AccountOverrideService(ILogger<AccountOverrideService> logger)
    {
        _logger = logger;
        _initialLoadTask = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (!File.Exists(_overridesPath)) return;
        try
        {
            var json = await File.ReadAllTextAsync(_overridesPath);
            _overrides = JsonSerializer.Deserialize<Dictionary<string, AccountOverride>>(
                json, _jsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erro ao carregar overrides de contas");
            _overrides = [];
        }
    }

    public async Task<AccountOverride?> GetOverrideAsync(string steamId64)
    {
        await _initialLoadTask;
        _overrides.TryGetValue(steamId64, out var o);
        return o;
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
            if (_overrides.Remove(steamId64))
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
