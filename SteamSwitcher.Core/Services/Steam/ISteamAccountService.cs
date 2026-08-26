using SteamSwitcher.Core.Models;

namespace SteamSwitcher.Core.Services;

public interface ISteamAccountService
{
    bool IsOperationInProgress { get; }
    Task<SteamAccountsSnapshot> GetSnapshotAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SteamAccount>> GetAccountsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SteamAccount>> GetAllAccountsAsync(CancellationToken ct = default);
    Task SwitchAccountAsync(SteamAccount account, LoginState? stateOverride = null, CancellationToken ct = default);
    Task<SteamAccount?> GetActiveAccountAsync(CancellationToken ct = default);
    Task ForgetAccountAsync(SteamAccount account, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ForgetAccountsAsync(
        IReadOnlyCollection<string> steamIds64,
        CancellationToken ct = default);
    Task<IReadOnlyList<string>> ForgetAccountsAsync(
        IReadOnlyCollection<SteamAccount> accounts,
        CancellationToken ct = default);
    Task AddAccountAsync(CancellationToken ct = default);
}
