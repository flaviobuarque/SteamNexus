using Microsoft.Extensions.Logging;
using SteamSwitcher.Core.Models;
using ValveKeyValue;

namespace SteamSwitcher.Core.Services;

public class SteamGameService(
    ISteamLocatorService locator,
    ISteamAccountService accountService,
    ILogger<SteamGameService> logger) : ISteamGameService
{
    private readonly string _steamPath = locator.FindSteamInstallPath() ?? string.Empty;

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
        // Troca de conta primeiro
        await accountService.SwitchAccountAsync(account, null, ct);

        // Aguarda Steam inicializar um pouco
        await Task.Delay(2000, ct);

        // Lança o jogo
        var uri = $"steam://rungameid/{game.AppId}";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = uri,
            UseShellExecute = true
        });

        logger.LogInformation("Jogo {Game} lançado na conta {Account}",
            game.Name, account.AccountName);
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
                CoverUrl = $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/header.jpg",
                HeroCoverUrl = $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/capsule_616x353.jpg",
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
}