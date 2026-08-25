namespace SteamSwitcher.Core.Services;

public sealed class SteamLocatorService(
    ISteamInstallationService installationService) : ISteamLocatorService
{
    public string? FindSteamInstallPath() =>
        installationService.SelectedInstallation?.RootPath;

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
