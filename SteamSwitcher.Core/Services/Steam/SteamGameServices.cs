using Microsoft.Extensions.Logging;
using SteamSwitcher.Core.Helpers;
using SteamSwitcher.Core.Models;
using ValveKeyValue;

namespace SteamSwitcher.Core.Services;

public class SteamGameService(
    ISteamLocatorService locator,
    ISteamInstallationService installationService,
    ISteamAccountService accountService,
    IAppSettingsService settingsService,
    ILogger<SteamGameService> logger) : ISteamGameService
{
    private readonly SemaphoreSlim _launchGate = new(1, 1);

    private static readonly string _gameLoginStatesPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamSwitcher", "game_loginstates.json");

    private static readonly string _manualCoversPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamSwitcher", "game_covers.json");

    public static readonly string ManualCoversDir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamSwitcher", "covers_manual");

    public IReadOnlyList<string> GetLibraryManifestDirectories() =>
        installationService.Installations
            .Where(installation => installation.IsValid)
            .SelectMany(installation => GetLibraryPaths(installation.RootPath))
            .Select(path => Path.Combine(path, "steamapps"))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public async Task<IReadOnlyList<SteamGame>> GetInstalledGamesAsync(
        IReadOnlyList<SteamAccount> accounts,
        CancellationToken ct = default)
    {
        var installations = installationService.Installations
            .Where(installation => installation.IsValid)
            .ToList();
        if (installations.Count == 0) return [];

        return await Task.Run(async () =>
        {
            var games = new List<SteamGame>();
            foreach (var installation in installations)
            {
                var installationAccounts = accounts
                    .Where(account => account.InstallationId == installation.Id)
                    .ToList();
                foreach (var libraryPath in GetLibraryPaths(installation.RootPath))
                {
                    var steamAppsPath = Path.Combine(libraryPath, "steamapps");
                    if (!Directory.Exists(steamAppsPath)) continue;

                    var manifests = Directory.GetFiles(steamAppsPath, "appmanifest_*.acf");
                    foreach (var manifest in manifests)
                    {
                        ct.ThrowIfCancellationRequested();
                        var game = ParseAppManifest(
                            manifest,
                            installationAccounts,
                            libraryPath,
                            installation);
                        if (game is not null)
                            games.Add(game);
                    }
                }
            }

            // Cada localconfig.vdf e lido uma única vez, mesmo quando a conta
            // possui centenas de jogos instalados.
            foreach (var ownerGroup in games
                .Where(g => g.OwnerAccount is not null)
                .GroupBy(g => new
                {
                    g.InstallationRootPath,
                    g.OwnerAccount!.SteamId32,
                }))
            {
                ct.ThrowIfCancellationRequested();
                var localConfigPath = locator.GetLocalConfigVdfPath(
                    ownerGroup.Key.InstallationRootPath,
                    ownerGroup.Key.SteamId32);
                var playtimes = ReadPlaytimes(localConfigPath);

                foreach (var game in ownerGroup)
                {
                    if (playtimes.TryGetValue(game.AppId, out var minutes))
                        game.PlaytimeMinutes = minutes;
                }
            }

            // Aplica preferência de status de login por jogo, se houver.
            var loginStates = await LoadGameLoginStatesAsync();
            var manualCovers = await LoadManualCoversAsync();
            foreach (var game in games)
            {
                if ((loginStates.TryGetValue(game.UniqueKey, out var rawState)
                        || loginStates.TryGetValue(game.AppId, out rawState))
                    && Enum.IsDefined(typeof(LoginState), rawState))
                {
                    game.LoginStateOverride = (LoginState)rawState;
                }

                if ((manualCovers.TryGetValue(game.UniqueKey, out var manualPath)
                        || manualCovers.TryGetValue(game.AppId, out manualPath))
                    && System.IO.File.Exists(manualPath))
                {
                    game.ManualCoverPath = manualPath;
                }
            }

            return (IReadOnlyList<SteamGame>)games
                .GroupBy(g => g.UniqueKey)
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
        await _launchGate.WaitAsync(ct);
        try
        {
            // Resolve precedência: per-game > per-account > global.
            // null na camada inferior cai como Online garanteed no serviço, mas repassamos explicito.
            var state = game.LoginStateOverride
                ?? account.LoginStateOverride
                ?? settingsService.Current.DefaultLoginStateOverride;

            var activeAccount = await accountService.GetActiveAccountAsync(ct);
            var accountAlreadyActive = string.Equals(
                activeAccount?.UniqueKey,
                account.UniqueKey,
                StringComparison.Ordinal);

            var stateAlreadyApplied = activeAccount?.WantsOfflineMode == (state == LoginState.Offline);
            if (!accountAlreadyActive || !stateAlreadyApplied)
            {
                // Troca de conta primeiro (passando o estado resolvido).
                await accountService.SwitchAccountAsync(account, state, ct);

                // Aguarda Steam inicializar um pouco somente após uma troca real.
                await Task.Delay(2000, ct);
            }
            else
            {
                SteamAccountSwitchPolicy.RequireRememberedAccount(activeAccount);
                logger.LogInformation(
                    "Conta {Account} já está ativa; reinicialização da Steam ignorada",
                    account.AccountName);
            }

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
        finally
        {
            _launchGate.Release();
        }
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

            var playtimes = ReadPlaytimes(localConfigPath);
            if (playtimes.TryGetValue(game.AppId, out var minutes))
                game.PlaytimeMinutes = minutes;
        }, ct);
    }

    private static Dictionary<string, int> ReadPlaytimes(string path)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return result;

        try
        {
            var depth = 0;
            var appsDepth = -1;
            var appDepth = -1;
            string? pendingSection = null;
            string? pendingAppId = null;
            string? currentAppId = null;

            foreach (var rawLine in File.ReadLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                    continue;

                if (line == "{")
                {
                    depth++;
                    if (pendingSection == "apps")
                    {
                        appsDepth = depth;
                        pendingSection = null;
                    }
                    else if (pendingAppId is not null)
                    {
                        currentAppId = pendingAppId;
                        pendingAppId = null;
                        appDepth = depth;
                    }
                    continue;
                }

                if (line == "}")
                {
                    if (depth == appDepth)
                    {
                        currentAppId = null;
                        appDepth = -1;
                    }
                    if (depth == appsDepth)
                        appsDepth = -1;
                    depth = Math.Max(0, depth - 1);
                    continue;
                }

                if (!TryReadVdfPair(line, out var key, out var value))
                    continue;

                if (value is null)
                {
                    if (string.Equals(key, "apps", StringComparison.OrdinalIgnoreCase))
                        pendingSection = "apps";
                    else if (appsDepth >= 0 && depth == appsDepth
                        && key.All(char.IsDigit))
                        pendingAppId = key;
                    continue;
                }

                if (currentAppId is not null
                    && string.Equals(key, "Playtime", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(value, out var minutes))
                {
                    result[currentAppId] = minutes;
                }
            }
        }
        catch
        {
            // Playtime é informação complementar e não bloqueia a lista de jogos.
        }

        return result;
    }

    private static bool TryReadVdfPair(
        string line,
        out string key,
        out string? value)
    {
        key = string.Empty;
        value = null;
        if (line.Length < 2 || line[0] != '"') return false;

        var keyEnd = line.IndexOf('"', 1);
        if (keyEnd < 0) return false;
        key = line[1..keyEnd];

        var remainder = line[(keyEnd + 1)..].TrimStart();
        if (remainder.Length == 0 || remainder[0] != '"') return true;

        var valueEnd = remainder.IndexOf('"', 1);
        if (valueEnd < 0) return false;
        value = remainder[1..valueEnd];
        return true;
    }

    // --- Privados ---

    private List<string> GetLibraryPaths(string steamPath)
    {
        var paths = new List<string> { steamPath };

        var libraryFoldersVdf = locator.GetLibraryFoldersVdfPath(steamPath);
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
        string libraryPath,
        SteamInstallation installation)
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
                InstallationId = installation.Id,
                InstallationName = installation.DisplayName,
                InstallationRootPath = installation.RootPath,
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
        return await AtomicJsonFile.ReadAsync(
            _gameLoginStatesPath,
            static () => new Dictionary<string, int>());
    }

    public async Task SetGameLoginStateAsync(string appId, LoginState? state, CancellationToken ct = default)
    {
        await AtomicJsonFile.UpdateAsync(
            _gameLoginStatesPath,
            static () => new Dictionary<string, int>(),
            map =>
            {
                if (state is null) map.Remove(appId);
                else map[appId] = (int)state.Value;
            },
            ct);
    }

    public async Task<Dictionary<string, string>> LoadManualCoversAsync()
    {
        return await AtomicJsonFile.ReadAsync(
            _manualCoversPath,
            static () => new Dictionary<string, string>());
    }

    public async Task SetManualCoverAsync(string appId, string? path, CancellationToken ct = default)
    {
        await AtomicJsonFile.UpdateAsync(
            _manualCoversPath,
            static () => new Dictionary<string, string>(),
            map =>
            {
                if (string.IsNullOrEmpty(path)) map.Remove(appId);
                else map[appId] = path;
            },
            ct);
    }

    public async Task ClearManualCoverAsync(string appId, CancellationToken ct = default)
    {
        string? coverToDelete = null;
        await AtomicJsonFile.UpdateAsync(
            _manualCoversPath,
            static () => new Dictionary<string, string>(),
            map =>
            {
                map.TryGetValue(appId, out coverToDelete);
                map.Remove(appId);
            },
            ct);

        try
        {
            if (!string.IsNullOrEmpty(coverToDelete) && File.Exists(coverToDelete))
                File.Delete(coverToDelete);
        }
        catch { }
    }
}
