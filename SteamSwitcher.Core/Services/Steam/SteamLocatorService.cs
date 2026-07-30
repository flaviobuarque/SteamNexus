using Microsoft.Win32;

namespace SteamSwitcher.Core.Services;

public class SteamLocatorService : ISteamLocatorService
{
    // O caminho de instalacao do Steam nao muda durante a execucao do app;
    // cacheamos apos a primeira resolucao para evitar chamadas repetidas
    // ao registry (que sao relativamente caras e invocadas em loops).
    private string? _cachedSteamPath;
    private bool _resolved;

    public string? FindSteamInstallPath()
    {
        if (_resolved) return _cachedSteamPath;

        // Tenta registry primeiro
        var regPath = Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
            "InstallPath", null) as string;

        if (!string.IsNullOrEmpty(regPath) && Directory.Exists(regPath))
        {
            _cachedSteamPath = regPath;
            _resolved = true;
            return regPath;
        }

        regPath = Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam",
            "InstallPath", null) as string;

        if (!string.IsNullOrEmpty(regPath) && Directory.Exists(regPath))
        {
            _cachedSteamPath = regPath;
            _resolved = true;
            return regPath;
        }

        // Fallback: paths comuns
        var defaults = new[]
        {
            @"C:\Program Files (x86)\Steam",
            @"C:\Program Files\Steam",
        };

        _cachedSteamPath = defaults.FirstOrDefault(Directory.Exists);
        _resolved = true;
        return _cachedSteamPath;
    }

    public string GetLoginUsersVdfPath(string steamPath) =>
        Path.Combine(steamPath, "config", "loginusers.vdf");

    public string GetUserDataPath(string steamPath) =>
        Path.Combine(steamPath, "userdata");

    public string GetLibraryFoldersVdfPath(string steamPath) =>
        Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");

    public string GetSteamExePath(string steamPath) =>
        Path.Combine(steamPath, "Steam.exe");

    public string GetLocalConfigVdfPath(string steamPath, string steamId32) =>
        Path.Combine(steamPath, "userdata", steamId32, "config", "localconfig.vdf");
}