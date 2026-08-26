namespace SteamSwitcher.Core.Models;

public class AppSettings
{
    // Aparência
    public AppTheme Theme { get; set; } = AppTheme.System;
    public AccountSortMode AccountSortMode { get; set; } = AccountSortMode.RecentUsage;
    public AccountViewMode AccountViewMode { get; set; } = AccountViewMode.Grid;
    public int AccountGridDensityPercent { get; set; } = 100;
    public GameSortMode GameSortMode { get; set; } = GameSortMode.Alphabetical;
    public GameViewMode GameViewMode { get; set; } = GameViewMode.Grid;
    public int GameGridDensityPercent { get; set; } = 100;

    // Comportamento após troca de conta
    public PostSwitchBehavior AfterAccountSwitch { get; set; } = PostSwitchBehavior.KeepOpen;
    public PostSwitchBehavior AfterGameLaunch { get; set; } = PostSwitchBehavior.KeepOpen;

    // Atalho global
    public bool IsGlobalHotkeyEnabled { get; set; }
    public HotkeyDefinition? GlobalHotkey { get; set; }

    // Status padrão global
    public LoginState? DefaultLoginStateOverride { get; set; }

    // Steam
    public bool StartSilent { get; set; } = true;
    public bool StartAsAdmin { get; set; } = false;
    public string? SteamApiKey { get; set; }
    public string? SteamInstallPath { get; set; }
    public List<string> KnownSteamInstallPaths { get; set; } = [];
    public Dictionary<string, string> SteamInstallationNames { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    // Cache
    public int AvatarCacheExpiryDays { get; set; } = 7;
    public int CoverCacheExpiryDays { get; set; } = 30;

    // SteamGridDB
    public string? SteamGridDbApiKey { get; set; }

}

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
    Win = 8
}

public sealed class HotkeyDefinition
{
    public HotkeyModifiers Modifiers { get; set; }

    // Código virtual do Windows; não depende de WPF.
    public int VirtualKey { get; set; }

    // Apenas para exibição e persistência legível.
    public string KeyName { get; set; } = string.Empty;

    public bool IsValid =>
        VirtualKey != 0 &&
        Modifiers != HotkeyModifiers.None &&
        !string.IsNullOrWhiteSpace(KeyName);

    public string DisplayText
    {
        get
        {
            if (!IsValid)
                return "Nenhum atalho definido";

            var parts = new List<string>();

            if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");

            parts.Add(KeyName);
            return string.Join(" + ", parts);
        }
    }
}

public enum AppTheme { Light, Dark, System }
public enum AccountSortMode { RecentUsage, Alphabetical }
public enum AccountViewMode { Grid, Compact }
public enum GameSortMode { Alphabetical, MostPlayed, LargestSize }
public enum GameViewMode { Grid, Compact }
public enum PostSwitchBehavior { MinimizeToTray, Close, KeepOpen }
