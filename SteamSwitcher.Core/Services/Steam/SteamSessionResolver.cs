using SteamSwitcher.Core.Models;

namespace SteamSwitcher.Core.Services;

public static class SteamSessionResolver
{
    // Only call for the installation whose Steam process is confirmed running.
    // Offline Steam can leave ActiveUser at zero even after a successful login.
    public static SteamAccount? Resolve(SteamAccountsSnapshot snapshot, string? activeUser)
    {
        if (!string.IsNullOrEmpty(activeUser) && activeUser != "0")
            return snapshot.Accounts.FirstOrDefault(a => a.SteamId32 == activeUser);

        var candidate = snapshot.ActiveAccount;
        return candidate is { RememberPassword: true, WantsOfflineMode: true }
            ? candidate : null;
    }
}
