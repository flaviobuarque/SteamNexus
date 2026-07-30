using SteamSwitcher.Core.Models;

namespace SteamSwitcher;

public static class DebugDemoData
{
#if DEBUG
    public static IReadOnlyList<SteamAccount>? TryCreateAccountsFromArgs()
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "--demo-accounts");

        if (index < 0 ||
            index + 1 >= args.Length ||
            !int.TryParse(args[index + 1], out var count) ||
            count <= 0)
        {
            return null;
        }

        return Enumerable.Range(1, count)
            .Select(index => new SteamAccount
            {
                SteamId64 = $"7656119{index:D10}",
                AccountName = $"conta_teste_{index:D4}",
                PersonaName = $"Conta de teste {index:D4}",
                MostRecent = index == 1,
                IsActive = index == 1,
                RememberPassword = true,
                Timestamp = DateTimeOffset.UtcNow
                    .AddMinutes(-index)
                    .ToUnixTimeSeconds()
            })
            .ToList();
    }
#else
    public static IReadOnlyList<SteamAccount>? TryCreateAccountsFromArgs() => null;
#endif
}