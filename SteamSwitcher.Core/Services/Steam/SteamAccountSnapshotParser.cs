using SteamSwitcher.Core.Models;
using ValveKeyValue;

namespace SteamSwitcher.Core.Services;

public static class SteamAccountSnapshotParser
{
    public static SteamAccountsSnapshot Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var accounts = new List<SteamAccount>();
        var serializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
        var data = serializer.Deserialize(stream);

        foreach (var user in data)
        {
            var account = new SteamAccount
            {
                SteamId64 = user.Name,
                AccountName = GetValue(user, "AccountName") ?? string.Empty,
                PersonaName = GetValue(user, "PersonaName") ?? string.Empty,
                RememberPassword = GetValue(user, "RememberPassword") == "1",
                MostRecent = GetValue(user, "MostRecent") == "1",
                AutoLogin = GetValue(user, "AutoLogin") == "1",
                WantsOfflineMode = GetValue(user, "WantsOfflineMode") == "1",
            };

            if (long.TryParse(
                    GetValue(user, "Timestamp"),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var timestamp))
            {
                account.Timestamp = timestamp;
            }

            accounts.Add(account);
        }

        var active = ResolveActiveAccount(accounts);
        foreach (var account in accounts)
            account.IsActive = account.SteamId64 == active?.SteamId64;

        return new SteamAccountsSnapshot(accounts, active);
    }

    public static SteamAccount? ResolveActiveAccount(
        IReadOnlyList<SteamAccount> accounts)
    {
        var autoLoginAccounts = accounts.Where(a => a.AutoLogin).ToList();
        if (autoLoginAccounts.Count > 0)
            return autoLoginAccounts.Count == 1 ? autoLoginAccounts[0] : null;

        var recentAccounts = accounts.Where(a => a.MostRecent).ToList();
        return recentAccounts.Count == 1 ? recentAccounts[0] : null;
    }

    private static string? GetValue(KVObject obj, string key)
    {
        return obj.Children
            .FirstOrDefault(child => string.Equals(child.Name, key, StringComparison.OrdinalIgnoreCase))
            ?.Value?.ToString();
    }
}
