using SteamSwitcher.Core.Helpers;
using SteamSwitcher.Core.Models;

namespace SteamSwitcher.Core.Services;

public static class SteamKnownAccountStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamSwitcher",
        "known_steam_accounts.json");

    public static Task<Dictionary<string, KnownSteamAccount>> LoadAsync(
        CancellationToken ct = default) =>
        AtomicJsonFile.ReadAsync(
            StorePath,
            static () => new Dictionary<string, KnownSteamAccount>(StringComparer.Ordinal),
            ct);

    public static Task RememberAsync(
        IEnumerable<SteamAccount> accounts,
        CancellationToken ct = default) =>
        AtomicJsonFile.UpdateAsync(
            StorePath,
            static () => new Dictionary<string, KnownSteamAccount>(StringComparer.Ordinal),
            stored =>
            {
                foreach (var account in accounts)
                {
                    if (string.IsNullOrWhiteSpace(account.InstallationId)
                        || string.IsNullOrWhiteSpace(account.SteamId64))
                        continue;

                    stored[account.UniqueKey] = new KnownSteamAccount
                    {
                        InstallationId = account.InstallationId,
                        SteamId64 = account.SteamId64,
                        AccountName = account.AccountName,
                        PersonaName = account.PersonaName,
                        Timestamp = account.Timestamp,
                    };
                }
            },
            ct);

    public static Task RemoveAsync(
        IEnumerable<string> uniqueKeys,
        CancellationToken ct = default) =>
        AtomicJsonFile.UpdateAsync(
            StorePath,
            static () => new Dictionary<string, KnownSteamAccount>(StringComparer.Ordinal),
            stored =>
            {
                foreach (var key in uniqueKeys)
                    stored.Remove(key);
            },
            ct);
}

public sealed class KnownSteamAccount
{
    public string InstallationId { get; set; } = string.Empty;
    public string SteamId64 { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string PersonaName { get; set; } = string.Empty;
    public long Timestamp { get; set; }
}
