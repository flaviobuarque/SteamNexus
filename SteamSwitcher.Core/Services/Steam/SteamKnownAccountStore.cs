using SteamSwitcher.Core.Helpers;
using SteamSwitcher.Core.Models;

namespace SteamSwitcher.Core.Services;

public interface ISteamKnownAccountStore
{
    Task<Dictionary<string, KnownSteamAccount>> LoadAsync(CancellationToken ct = default);
    Task RememberAsync(IEnumerable<SteamAccount> accounts, CancellationToken ct = default);
    Task RemoveAsync(IEnumerable<string> uniqueKeys, CancellationToken ct = default);
}

public sealed class SteamKnownAccountStore : ISteamKnownAccountStore
{
    private readonly string _storePath;

    public SteamKnownAccountStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteamSwitcher",
            "known_steam_accounts.json"))
    {
    }

    public SteamKnownAccountStore(string storePath) => _storePath = storePath;

    public Task<Dictionary<string, KnownSteamAccount>> LoadAsync(
        CancellationToken ct = default) =>
        AtomicJsonFile.ReadAsync(
            _storePath,
            static () => new Dictionary<string, KnownSteamAccount>(StringComparer.Ordinal),
            ct);

    public Task RememberAsync(
        IEnumerable<SteamAccount> accounts,
        CancellationToken ct = default) =>
        AtomicJsonFile.UpdateAsync(
            _storePath,
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

    public Task RemoveAsync(
        IEnumerable<string> uniqueKeys,
        CancellationToken ct = default) =>
        AtomicJsonFile.UpdateAsync(
            _storePath,
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
