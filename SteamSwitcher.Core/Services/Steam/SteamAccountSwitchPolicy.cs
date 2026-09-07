using System.Diagnostics.CodeAnalysis;
using SteamSwitcher.Core.Models;

namespace SteamSwitcher.Core.Services;

public static class SteamAccountSwitchPolicy
{
    public static SteamAccount ResolveRememberedAccount(
        IEnumerable<SteamAccount> freshAccounts, string uniqueKey)
    {
        var account = freshAccounts.FirstOrDefault(candidate => candidate.UniqueKey == uniqueKey);
        RequireRememberedAccount(account);
        return account;
    }

    // Always pass a freshly read VDF account, not a cached card or archived entry.
    public static void RequireRememberedAccount([NotNull] SteamAccount? account)
    {
        if (account is null)
            throw new InvalidOperationException(
                "A conta não está disponível no loginusers.vdf. Entre nela pela Steam antes de continuar.");
    }
}
