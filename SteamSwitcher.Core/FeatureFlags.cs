namespace SteamSwitcher.Core;

/// <summary>
/// Toggles de features incompletas ou fora do release.
/// Altere para <c>true</c> para reativar UI e comportamento.
/// </summary>
public static class FeatureFlags
{
    /// <summary>
    /// Steam Web API Key nas configurações e chamadas de bans/conquistas via Web API.
    /// </summary>
    public static readonly bool SteamWebApiKey = false;

    /// <summary>
    /// Página Modificações e monitoramento de pastas de mods.
    /// </summary>
    public static readonly bool Mods = false;

    /// <summary>
    /// Página Backup de Saves e orquestração de backup automático.
    /// </summary>
    public static readonly bool Backup = false;
}
