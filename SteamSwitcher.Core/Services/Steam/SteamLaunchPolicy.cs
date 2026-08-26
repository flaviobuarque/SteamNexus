namespace SteamSwitcher.Core.Services;

public static class SteamLaunchPolicy
{
    public static bool ShouldLaunchAsDesktopUser(
        bool currentProcessIsElevated,
        bool startAsAdmin) => currentProcessIsElevated && !startAsAdmin;
}
