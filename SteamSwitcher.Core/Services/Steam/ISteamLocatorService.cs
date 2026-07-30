namespace SteamSwitcher.Core.Services;

public interface ISteamLocatorService
{
    string? FindSteamInstallPath();
    string GetLoginUsersVdfPath(string steamPath);
    string GetUserDataPath(string steamPath);
    string GetLibraryFoldersVdfPath(string steamPath);
    string GetSteamExePath(string steamPath);
    string GetLocalConfigVdfPath(string steamPath, string steamId32);
}