using Microsoft.Extensions.Logging;
using SteamSwitcher.Core.Models;
using System.Text.Json;
using SteamSwitcher.Core.Helpers;

namespace SteamSwitcher.Core.Services;

public class AppSettingsService(ILogger<AppSettingsService> logger, string? settingsPath = null) : IAppSettingsService
{
    private readonly string _settingsPath = settingsPath ?? Path.Combine(
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
        try
        {
            Current = await RecoverableJsonFile.ReadAsync<AppSettings>(_settingsPath, _jsonOptions, () =>
            {
                logger.LogWarning("Settings recovered from the last valid backup");
                ActivityJournal.Shared.Record(ActivityKind.SettingsRecovered);
            }) ?? new();
            Current.KnownSteamInstallPaths ??= [];
            Current.SteamInstallationNames ??= new(StringComparer.OrdinalIgnoreCase);
            Current.ThemeLibrary ??= [];
            if (Current.ThemeLibrary.Count == 0 && Current.CustomTheme is not null)
            {
                var migrated = new SavedTheme { Theme = Current.CustomTheme.Clone() };
                Current.ThemeLibrary.Add(migrated);
                if (Current.CustomTheme.IsEnabled && string.IsNullOrEmpty(Current.ActiveThemePresetName))
                    Current.ActiveCustomThemeId = migrated.Id;
                await SaveAsync(Current);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Erro ao carregar configurações, usando padrões");
            Current = new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        await RecoverableJsonFile.WriteAsync(_settingsPath, settings, _jsonOptions);
        Current = settings;
    }
}
