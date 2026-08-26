namespace SteamSwitcher.Core.Models;

public sealed class CustomThemeSettings
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string Name { get; set; } = "Meu tema";
    public string Author { get; set; } = string.Empty;
    public AppTheme BaseTheme { get; set; } = AppTheme.Dark;
    public bool IsEnabled { get; set; }

    public string Background { get; set; } = "#07111F";
    public string Chrome { get; set; } = "#081827";
    public string Surface { get; set; } = "#0D2135";
    public string SurfaceAlt { get; set; } = "#102B45";
    public string SurfaceHover { get; set; } = "#16385D";
    public string Border { get; set; } = "#1F456A";
    public string Focus { get; set; } = "#408EE3";
    public string TextPrimary { get; set; } = "#EAF7FF";
    public string TextSecondary { get; set; } = "#A9C8DE";
    public string TextMuted { get; set; } = "#6593B0";
    public string Accent { get; set; } = "#408EE3";
    public string AccentAlt { get; set; } = "#3CCEFD";
    public string AccentSurface { get; set; } = "#102C4C";
    public string Success { get; set; } = "#57E389";
    public string Warning { get; set; } = "#F2B84B";
    public string Danger { get; set; } = "#EF6A6A";

    public string? BackgroundImagePath { get; set; }
    public double BackgroundImageOpacity { get; set; } = 0.25;
    public string BackgroundOverlay { get; set; } = "#6607111F";
    public string BackgroundStretch { get; set; } = "UniformToFill";

    public double CardCornerRadius { get; set; } = 14;
    public double ButtonCornerRadius { get; set; } = 8;
    public double BorderOpacity { get; set; } = 1;
    public bool ShadowsEnabled { get; set; } = true;

    public CustomThemeSettings Clone() => (CustomThemeSettings)MemberwiseClone();

    public static CustomThemeSettings CreateDark() => new();

    public static CustomThemeSettings CreateLight() => new()
    {
        BaseTheme = AppTheme.Light,
        Background = "#F1F5F9",
        Chrome = "#E6EDF5",
        Surface = "#FFFFFF",
        SurfaceAlt = "#EDF2F7",
        SurfaceHover = "#E8F1FC",
        Border = "#B8C5D1",
        Focus = "#2563EB",
        TextPrimary = "#0B1220",
        TextSecondary = "#334155",
        TextMuted = "#64748B",
        Accent = "#2563EB",
        AccentAlt = "#0891B2",
        AccentSurface = "#DBEAFE",
        Success = "#15803D",
        Warning = "#B45309",
        Danger = "#DC2626",
        BackgroundOverlay = "#66F1F5F9",
    };
}
