using System.Diagnostics.CodeAnalysis;
using SteamSwitcher.Core.Models;

namespace SteamSwitcher.Core.Services;

public static class SteamAccountSwitchPolicy
{
    // Always pass a freshly read VDF account, not a cached card or archived entry.
    public static void RequireRememberedAccount([NotNull] SteamAccount? account)
    {
        if (account is null || !account.RememberPassword)
            throw new InvalidOperationException(
                "Esta conta não está disponível para troca: é necessário RememberPassword=1 no loginusers.vdf. " +
                "Entre novamente nessa conta pela Steam com a opção de lembrar o login ativada. " +
                "O SteamNexus não altera essa preferência.");
    }
}
