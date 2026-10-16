namespace SteamSwitcher.Core.Services;

public interface ISteamRegistryService
{
    string? ReadActiveUser();
    void SetAutoLoginUser(string? username);
    bool? ReadAppRunning(string appId);
    string? ReadSteamPath();
}
