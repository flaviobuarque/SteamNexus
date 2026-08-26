namespace SteamSwitcher.Core.Models;

public class SteamAccount
{
    // Instalação de origem. SteamId64 pode existir em mais de uma instalação.
    public string InstallationId { get; set; } = string.Empty;
    public string InstallationName { get; set; } = string.Empty;
    public string InstallationRootPath { get; set; } = string.Empty;

    // Dados do loginusers.vdf
    public required string SteamId64 { get; init; }
    public required string AccountName { get; set; }
    public string PersonaName { get; set; } = string.Empty;
    public bool RememberPassword { get; set; }
    public bool MostRecent { get; set; }
    public bool AutoLogin { get; set; }
    public long Timestamp { get; set; }
    public bool WantsOfflineMode { get; set; }

    // Dados remotos (avatar)
    public string? AvatarUrl { get; set; }

    // Overrides locais do app
    public string? CustomDisplayName { get; set; }
    public string? CustomAvatarPath { get; set; }

    // Preferência de status por conta
    public LoginState? LoginStateOverride { get; set; }

    // Organização local do SteamSwitcher
    public bool IsFavorite { get; set; }

    // Computed
    public string DisplayName => CustomDisplayName ?? PersonaName;
    public string AvatarSource => CustomAvatarPath ?? AvatarUrl ?? string.Empty;
    public bool IsActive { get; set; }
    public string UniqueKey => string.IsNullOrWhiteSpace(InstallationId)
        ? SteamId64
        : $"{InstallationId}:{SteamId64}";

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
