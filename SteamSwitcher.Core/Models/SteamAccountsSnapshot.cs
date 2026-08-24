namespace SteamSwitcher.Core.Models;

public sealed record SteamAccountsSnapshot(
    IReadOnlyList<SteamAccount> Accounts,
    SteamAccount? ActiveAccount)
{
    public static SteamAccountsSnapshot Empty { get; } = new([], null);
}
