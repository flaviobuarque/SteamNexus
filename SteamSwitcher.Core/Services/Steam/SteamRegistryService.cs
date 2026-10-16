using Microsoft.Win32;

namespace SteamSwitcher.Core.Services;

public class SteamRegistryService : ISteamRegistryService
{
    private const string SteamKeyPath = @"Software\Valve\Steam";
    private const string SteamFullKey = @"HKEY_CURRENT_USER\Software\Valve\Steam";

    public string? ReadActiveUser()
    {
        try
        {
            return Registry.GetValue(SteamFullKey, "AutoLoginUser", null) as string;
        }
        catch
        {
            return null;
        }
    }

    public void SetAutoLoginUser(string? username)
    {
        try
        {
            Registry.SetValue(SteamFullKey, "AutoLoginUser", username ?? string.Empty);
        }
        catch
        {
            // Logging or fallback
        }
    }

    public bool? ReadAppRunning(string appId)
    {
        if (!uint.TryParse(appId, out _)) return null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Valve\Steam\Apps\{appId}");
            return Convert.ToInt32(key?.GetValue("Running") ?? 0) != 0;
        }
        catch
        {
            return null;
        }
    }

    public string? ReadSteamPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SteamKeyPath);
            return key?.GetValue("SteamPath") as string;
        }
        catch
        {
            return null;
        }
    }
}
