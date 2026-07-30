namespace SteamSwitcher.Core.Models;

public class SteamAccount
{
    // Dados do loginusers.vdf
    public required string SteamId64 { get; init; }
    public required string AccountName { get; init; }
    public string PersonaName { get; set; } = string.Empty;
    public bool RememberPassword { get; set; }
    public bool MostRecent { get; set; }
    public long Timestamp { get; set; }
    public bool WantsOfflineMode { get; set; }

    // Dados remotos (avatar, VAC)
    public string? AvatarUrl { get; set; }
    public bool IsVacBanned { get; set; }
    public bool IsGameBanned { get; set; }
    public bool IsLimitedAccount { get; set; }

    // Overrides locais do app
    public string? CustomDisplayName { get; set; }
    public string? CustomAvatarPath { get; set; }

    // Preferência de status por conta
    public LoginState? LoginStateOverride { get; set; }

    // Computed
    public string DisplayName => CustomDisplayName ?? PersonaName;
    public string AvatarSource => CustomAvatarPath ?? AvatarUrl ?? string.Empty;
    public bool IsActive { get; set; }

    // SteamID32 para paths de userdata
    public string SteamId32
    {
        get
        {
            if (!ulong.TryParse(SteamId64, out var id64)) return string.Empty;
            return ((uint)(id64 & 0xFFFFFFFF)).ToString();
        }
    }
}