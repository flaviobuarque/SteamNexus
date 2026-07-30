using SteamSwitcher.Core.Models;

namespace SteamSwitcher.Core.Services;

public interface IAccountOverrideService
{
    Task<AccountOverride?> GetOverrideAsync(string steamId64);
    Task SaveOverrideAsync(string steamId64, AccountOverride data);
    Task RemoveOverrideAsync(string steamId64);
}

public class AccountOverride
{
    public string? CustomDisplayName { get; set; }
    public string? CustomAvatarPath { get; set; }
    public LoginState? LoginStateOverride { get; set; }
}