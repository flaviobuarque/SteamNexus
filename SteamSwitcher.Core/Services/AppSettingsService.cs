using System.Text.Json;
using Microsoft.Extensions.Logging;
using SteamSwitcher.Core.Models;

namespace SteamSwitcher.Core.Services;

public class AppSettingsService(ILogger<AppSettingsService> logger) : IAppSettingsService
{
    private static readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamSwitcher", "settings.json");

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public AppSettings Current { get; private set; } = new();

    public async Task LoadAsync()
    {
        if (!File.Exists(_settingsPath))
        {
            Current = new AppSettings();
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_settingsPath);
            Current = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Erro ao carregar configurações, usando padrões");
            Current = new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        Current = settings;
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        await File.WriteAllTextAsync(_settingsPath, json);
    }
}