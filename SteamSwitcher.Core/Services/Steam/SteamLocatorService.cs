using Microsoft.Win32;

namespace SteamSwitcher.Core.Services;

public class SteamLocatorService : ISteamLocatorService
{
    public string? FindSteamInstallPath()
    {
        // Tenta registry primeiro
        var regPath = Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
            "InstallPath", null) as string;

        if (!string.IsNullOrEmpty(regPath) && Directory.Exists(regPath))
            return regPath;

        regPath = Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam",
            "InstallPath", null) as string;

        if (!string.IsNullOrEmpty(regPath) && Directory.Exists(regPath))
            return regPath;

        // Fallback: paths comuns
        var defaults = new[]
        {
            @"C:\Program Files (x86)\Steam",
            @"C:\Program Files\Steam",
        };

        return defaults.FirstOrDefault(Directory.Exists);
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