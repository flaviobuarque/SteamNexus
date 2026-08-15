using Microsoft.Extensions.Logging;
using SteamSwitcher.Core.Models;
using ValveKeyValue;

namespace SteamSwitcher.Core.Services;

public class SteamGameService(
    ISteamLocatorService locator,
    ISteamAccountService accountService,
    IAppSettingsService settingsService,
    ILogger<SteamGameService> logger) : ISteamGameService
{
    private readonly string _steamPath = locator.FindSteamInstallPath() ?? string.Empty;

    private static readonly string _gameLoginStatesPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamSwitcher", "game_loginstates.json");

    private static readonly string _manualCoversPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamSwitcher", "game_covers.json");

    public static readonly string ManualCoversDir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamSwitcher", "covers_manual");

    public async Task<IReadOnlyList<SteamGame>> GetInstalledGamesAsync(
        IReadOnlyList<SteamAccount> accounts,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_steamPath)) return [];

        return await Task.Run(async () =>
        {
            var games = new List<SteamGame>();
            var libraryPaths = GetLibraryPaths();

            foreach (var libraryPath in libraryPaths)
            {
                var steamAppsPath = Path.Combine(libraryPath, "steamapps");
                if (!Directory.Exists(steamAppsPath)) continue;

                var manifests = Directory.GetFiles(steamAppsPath, "appmanifest_*.acf");
                foreach (var manifest in manifests)
                {
                    ct.ThrowIfCancellationRequested();
                    var game = ParseAppManifest(manifest, accounts, libraryPath);
                    if (game is not null)
                        games.Add(game);
                }
            }

            // Carrega playtime para cada conta
            foreach (var game in games)
            {
                if (game.OwnerAccount is not null)
                    await LoadPlaytimeAsync(game, _steamPath, ct);
            }

            // Aplica preferência de status de login por jogo, se houver.
            var loginStates = await LoadGameLoginStatesAsync();
            var manualCovers = await LoadManualCoversAsync();
            foreach (var game in games)
            {
                if (loginStates.TryGetValue(game.AppId, out var rawState)
                    && Enum.IsDefined(typeof(LoginState), rawState))
                {
                    game.LoginStateOverride = (LoginState)rawState;
                }

                if (manualCovers.TryGetValue(game.AppId, out var manualPath)
                    && System.IO.File.Exists(manualPath))
                {
                    game.ManualCoverPath = manualPath;
                }
            }

            return (IReadOnlyList<SteamGame>)games
                .GroupBy(g => g.AppId)
                .Select(g => g.First())
                .OrderBy(g => g.Name)
                .ToList();
        }, ct);
    }

    public async Task LaunchGameAsync(
        SteamGame game,
        SteamAccount account,
        CancellationToken ct = default)
    {
        // Resolve precedência: per-game > per-account > global.
        // null na camada inferior cai como Online garanteed no serviço, mas repassamos explicito.
        var state = game.LoginStateOverride
            ?? account.LoginStateOverride
            ?? settingsService.Current.DefaultLoginStateOverride;

        // Troca de conta primeiro (passando o estado resolvido).
        await accountService.SwitchAccountAsync(account, state, ct);

        // Aguarda Steam inicializar um pouco
        await Task.Delay(2000, ct);

        // Lança o jogo
        var uri = $"steam://rungameid/{game.AppId}";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = uri,
            UseShellExecute = true
        });

        logger.LogInformation("Jogo {Game} lançado na conta {Account} (estado {State})",
            game.Name, account.AccountName, state);
    }

    public async Task LoadPlaytimeAsync(
        SteamGame game,
        string steamPath,
        CancellationToken ct = default)
    {
        if (game.OwnerAccount is null) return;

        await Task.Run(() =>
        {
            var localConfigPath = locator.GetLocalConfigVdfPath(
                steamPath, game.OwnerAccount.SteamId32);

            if (!File.Exists(localConfigPath)) return;

            try
            {
                using var stream = File.OpenRead(localConfigPath);
                var kv = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
                var data = kv.Deserialize(stream);

                var appsNode = data["Software"]?["Valve"]?["Steam"]?["apps"];
                if (appsNode is null) return;

                var appNode = appsNode[game.AppId];
                if (appNode is null) return;

                if (int.TryParse(appNode["Playtime"]?.ToString(), out var minutes))
                    game.PlaytimeMinutes = minutes;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Erro ao ler playtime de {AppId}", game.AppId);
            }
        }, ct);
    }

    // --- Privados ---

    private List<string> GetLibraryPaths()
    {
        var paths = new List<string> { _steamPath };

        var libraryFoldersVdf = locator.GetLibraryFoldersVdfPath(_steamPath);
        if (!File.Exists(libraryFoldersVdf)) return paths;

        try
        {
            using var stream = File.OpenRead(libraryFoldersVdf);
            var kv = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
            var data = kv.Deserialize(stream);

            foreach (var entry in data)
            {
                var path = entry["path"]?.ToString();
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    paths.Add(path);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Erro ao ler libraryfolders.vdf");
        }

        return paths;
    }

    private SteamGame? ParseAppManifest(
    string manifestPath,
    IReadOnlyList<SteamAccount> accounts,
    string libraryPath)
    {
        try
        {
            using var stream = File.OpenRead(manifestPath);
            var kv = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
            var data = kv.Deserialize(stream);

            var appId = data["appid"]?.ToString();
            var name = data["name"]?.ToString();
            var installDir = data["installdir"]?.ToString();
            var lastOwner = data["LastOwner"]?.ToString();
            var sizeOnDisk = data["SizeOnDisk"]?.ToString();

            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(name))
                return null;

            // Filtra softwares e ferramentas da Steam
            if (IsSoftwareOrTool(name))
                return null;

            var owner = accounts.FirstOrDefault(a => a.SteamId64 == lastOwner);

            if (!long.TryParse(sizeOnDisk, out var sizeBytes))
                sizeBytes = 0;

            var game = new SteamGame
            {
                AppId = appId,
                Name = name,
                InstallDir = installDir ?? string.Empty,
                LibraryPath = libraryPath,
                SizeOnDisk = sizeBytes,
                OwnerSteamId64 = lastOwner,
                OwnerAccount = owner,
            };

            return game;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Erro ao parsear {Manifest}", manifestPath);
            return null;
        }
    }

    private static bool IsSoftwareOrTool(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower.Contains("steamworks")
            || lower.Contains("redistributable")
            || lower.Contains("directx")
            || lower.Contains("physx")
            || lower.Contains(" sdk")
            || lower.Contains("openal")
            || lower.Contains("vcredist")
            || lower.Contains("dotnet")
            || lower.StartsWith("steam linux")
            || lower.StartsWith("proton");
    }

    // --- Preferência de status de login por jogo ---

    public async Task<Dictionary<string, int>> LoadGameLoginStatesAsync()
    {
        try
        {
            if (!System.IO.File.Exists(_gameLoginStatesPath)) return [];
            var raw = await System.IO.File.ReadAllTextAsync(_gameLoginStatesPath);
            return System.Text.Json.JsonSerializer
                .Deserialize<Dictionary<string, int>>(raw) ?? [];
        }
        catch { return []; }
    }

    public async Task SetGameLoginStateAsync(string appId, LoginState? state, CancellationToken ct = default)
    {
        var map = await LoadGameLoginStatesAsync();
        if (state is null)
            map.Remove(appId);
        else
            map[appId] = (int)state.Value;

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_gameLoginStatesPath)!);
        await System.IO.File.WriteAllTextAsync(_gameLoginStatesPath,
            System.Text.Json.JsonSerializer.Serialize(map,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
            ct);
    }

    public async Task<Dictionary<string, string>> LoadManualCoversAsync()
    {
        try
        {
            if (!System.IO.File.Exists(_manualCoversPath)) return [];
            var raw = await System.IO.File.ReadAllTextAsync(_manualCoversPath);
            return System.Text.Json.JsonSerializer
                .Deserialize<Dictionary<string, string>>(raw) ?? [];
        }
        catch { return []; }
    }

    public async Task SetManualCoverAsync(string appId, string? path, CancellationToken ct = default)
    {
        var map = await LoadManualCoversAsync();
        if (string.IsNullOrEmpty(path))
            map.Remove(appId);
        else
            map[appId] = path;

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_manualCoversPath)!);
        await System.IO.File.WriteAllTextAsync(_manualCoversPath,
            System.Text.Json.JsonSerializer.Serialize(map,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
            ct);
    }

    public async Task ClearManualCoverAsync(string appId, CancellationToken ct = default)
    {
        var map = await LoadManualCoversAsync();
        if (map.TryGetValue(appId, out var path))
        {
            try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
            catch { }
        }
        map.Remove(appId);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_manualCoversPath)!);
        await System.IO.File.WriteAllTextAsync(_manualCoversPath,
            System.Text.Json.JsonSerializer.Serialize(map,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
            ct);
    }
}