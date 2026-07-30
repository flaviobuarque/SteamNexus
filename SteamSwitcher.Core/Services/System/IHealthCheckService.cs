using SteamSwitcher.Core.Models;

namespace SteamSwitcher.Core.Services;

public interface IHealthCheckService
{
    Task<AccountHealth> CheckAccountAsync(SteamAccount account, CancellationToken ct = default);
    Task<IReadOnlyList<AccountHealth>> CheckAllAccountsAsync(
        IReadOnlyList<SteamAccount> accounts, CancellationToken ct = default);
}

public class AccountHealth
{
    public required string SteamId64 { get; init; }
    public bool HasValidSession { get; set; }
    public bool IsVacBanned { get; set; }
    public bool IsGameBanned { get; set; }
    public bool IsLimitedAccount { get; set; }
    public bool IsCommunityBanned { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}