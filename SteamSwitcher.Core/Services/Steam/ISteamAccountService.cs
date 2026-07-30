using SteamSwitcher.Core.Models;

namespace SteamSwitcher.Core.Services;

public interface ISteamAccountService
{
    Task<IReadOnlyList<SteamAccount>> GetAccountsAsync(CancellationToken ct = default);
    Task SwitchAccountAsync(SteamAccount account, LoginState? stateOverride = null, CancellationToken ct = default);
    Task<SteamAccount?> GetActiveAccountAsync(CancellationToken ct = default);
    void ForgetAccount(SteamAccount account);
    Task AddAccountAsync(CancellationToken ct = default);
}