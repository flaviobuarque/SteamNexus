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
        if (account is null || !account.RememberPassword)
            throw new InvalidOperationException(
                "Esta conta não está disponível para troca: é necessário RememberPassword=1 no loginusers.vdf. " +
                "Entre novamente nessa conta pela Steam com a opção de lembrar o login ativada. " +
                "Se já usou a correção no diagnóstico, verifique se a Steam manteve essa opção ativada.");
    }
}
