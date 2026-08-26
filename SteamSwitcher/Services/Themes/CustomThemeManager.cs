using SteamSwitcher.Core.Models;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SteamSwitcher.Services.Themes;

public static class CustomThemeManager
{
    private static ResourceDictionary? _customResources;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void Apply(CustomThemeSettings? theme)
    {
        if (_customResources is not null)
            Application.Current.Resources.MergedDictionaries.Remove(_customResources);
        _customResources = null;

        if (theme is not { IsEnabled: true }) return;
        Validate(theme);

        var resources = new ResourceDictionary();
        AddBrush(resources, "AppBackgroundBrush", theme.Background);
        AddBrush(resources, "AppChromeBrush", theme.Chrome);
        AddBrush(resources, "AppSurfaceBrush", theme.Surface);
        AddBrush(resources, "AppSurfaceAltBrush", theme.SurfaceAlt);
        AddBrush(resources, "AppSurfaceHoverBrush", theme.SurfaceHover);
        AddBrush(resources, "AppBorderBrush", theme.Border, theme.BorderOpacity);
        AddBrush(resources, "AppBorderStrongBrush", theme.Focus);
        AddBrush(resources, "AppTextPrimaryBrush", theme.TextPrimary);
        AddBrush(resources, "AppTextSecondaryBrush", theme.TextSecondary);
        AddBrush(resources, "AppTextMutedBrush", theme.TextMuted);
        AddBrush(resources, "AppAccentBrush", theme.Accent);
        AddBrush(resources, "AppAccentAltBrush", theme.AccentAlt);
        AddBrush(resources, "AppAccentSurfaceBrush", theme.AccentSurface);
        AddBrush(resources, "AppSuccessBrush", theme.Success);
        AddBrush(resources, "AppWarningBrush", theme.Warning);
        AddBrush(resources, "AppDangerBrush", theme.Danger);

        AddAliases(resources, theme);
        resources["AppCardCornerRadius"] = new CornerRadius(Clamp(theme.CardCornerRadius, 0, 28));
        resources["AppButtonCornerRadius"] = new CornerRadius(Clamp(theme.ButtonCornerRadius, 0, 20));
        resources["AppThemeShadowsEnabled"] = theme.ShadowsEnabled;

        if (!string.IsNullOrWhiteSpace(theme.BackgroundImagePath)
            && File.Exists(theme.BackgroundImagePath))
            resources["AppBackgroundBrush"] = CreateBackgroundBrush(theme);

        _customResources = resources;
        Application.Current.Resources.MergedDictionaries.Add(resources);
    }

    public static async Task ExportAsync(CustomThemeSettings theme, string path, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(theme, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }

    public static async Task<CustomThemeSettings> ImportAsync(string path, CancellationToken ct = default)
    {
        var json = await File.ReadAllTextAsync(path, ct);
        var theme = JsonSerializer.Deserialize<CustomThemeSettings>(json, JsonOptions)
            ?? throw new InvalidDataException("O arquivo de tema está vazio ou inválido.");
        Validate(theme);
        return theme;
    }

    public static string CopyBackgroundToLibrary(string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp"))
            throw new InvalidDataException("Use uma imagem PNG, JPG, WEBP ou BMP.");

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteamSwitcher", "Themes", "Assets");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, $"background-{Guid.NewGuid():N}{extension}");
        File.Copy(sourcePath, destination, false);
        return destination;
    }

    public static double ContrastRatio(string foreground, string background)
    {
        var light = RelativeLuminance(ParseColor(foreground));
        var dark = RelativeLuminance(ParseColor(background));
        return (Math.Max(light, dark) + 0.05) / (Math.Min(light, dark) + 0.05);
    }

    public static void Validate(CustomThemeSettings theme)
    {
        if (theme.FormatVersion > CustomThemeSettings.CurrentFormatVersion)
            throw new InvalidDataException("Este tema foi criado para uma versão mais nova do SteamNexus.");
        if (theme.BaseTheme == AppTheme.System)
            throw new InvalidDataException("Um tema personalizado deve usar base clara ou escura.");

        foreach (var value in ColorValues(theme)) ParseColor(value);
        theme.BackgroundImageOpacity = Clamp(theme.BackgroundImageOpacity, 0, 1);
        theme.BorderOpacity = Clamp(theme.BorderOpacity, 0.2, 1);
        theme.CardCornerRadius = Clamp(theme.CardCornerRadius, 0, 28);
        theme.ButtonCornerRadius = Clamp(theme.ButtonCornerRadius, 0, 20);
        if (string.IsNullOrWhiteSpace(theme.Name)) theme.Name = "Tema personalizado";
    }

    private static void AddAliases(ResourceDictionary resources, CustomThemeSettings theme)
    {
        AddBrush(resources, "ApplicationBackgroundBrush", theme.Background);
        AddBrush(resources, "ControlFillColorDefaultBrush", theme.Surface);
        AddBrush(resources, "ControlFillColorSecondaryBrush", theme.SurfaceAlt);
        AddBrush(resources, "ControlFillColorTertiaryBrush", theme.SurfaceHover);
        AddBrush(resources, "ControlStrokeColorDefaultBrush", theme.Border, theme.BorderOpacity);
        AddBrush(resources, "ControlStrokeColorSecondaryBrush", theme.Border, theme.BorderOpacity * 0.75);
        AddBrush(resources, "SystemAccentColorPrimaryBrush", theme.Accent);
        resources["SystemAccentColor"] = ParseColor(theme.Accent);
        AddBrush(resources, "TextFillColorPrimaryBrush", theme.TextPrimary);
        AddBrush(resources, "TextFillColorSecondaryBrush", theme.TextSecondary);
        AddBrush(resources, "TextFillColorTertiaryBrush", theme.TextMuted);
        AddBrush(resources, "ComboBoxDropDownBackground", theme.Surface);
        AddBrush(resources, "ComboBoxItemForeground", theme.TextPrimary);
        AddBrush(resources, "ComboBoxItemBackgroundSelected", theme.AccentSurface);
        AddBrush(resources, "ComboBoxItemBackgroundPointerOver", theme.SurfaceHover);
        AddBrush(resources, "ComboBoxBackground", theme.Surface);
        AddBrush(resources, "ComboBoxBackgroundUnfocused", theme.Surface);
        AddBrush(resources, "ComboBoxBorderBrush", theme.Border);
        AddBrush(resources, "ComboBoxForeground", theme.TextPrimary);
        AddBrush(resources, "ContextMenuBackground", theme.Surface);
        AddBrush(resources, "ContextMenuBorderBrush", theme.Border);
        AddBrush(resources, "MenuItemHighlightBrush", theme.SurfaceHover);
        AddBrush(resources, "MenuItemHighlightedForeground", theme.TextPrimary);
        AddBrush(resources, "AccountCardHeaderBrush", theme.SurfaceHover);
        AddBrush(resources, "AccountCardSeparatorBrush", theme.Border);
    }

    private static Brush CreateBackgroundBrush(CustomThemeSettings theme)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(theme.BackgroundImagePath!, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();

        var image = new ImageDrawing(bitmap, new Rect(0, 0, 1, 1));
        var imageLayer = new DrawingGroup { Opacity = theme.BackgroundImageOpacity };
        imageLayer.Children.Add(image);
        var drawings = new DrawingGroup();
        drawings.Children.Add(new GeometryDrawing(
            new SolidColorBrush(ParseColor(theme.Background)), null,
            new RectangleGeometry(new Rect(0, 0, 1, 1))));
        drawings.Children.Add(imageLayer);
        drawings.Children.Add(new GeometryDrawing(
            new SolidColorBrush(ParseColor(theme.BackgroundOverlay)), null,
            new RectangleGeometry(new Rect(0, 0, 1, 1))));

        return new DrawingBrush(drawings)
        {
            Stretch = Enum.TryParse<Stretch>(theme.BackgroundStretch, out var stretch)
                ? stretch : Stretch.UniformToFill,
        };
    }

    private static void AddBrush(ResourceDictionary resources, string key, string value, double opacity = 1)
        => resources[key] = new SolidColorBrush(ParseColor(value)) { Opacity = Clamp(opacity, 0, 1) };

    private static Color ParseColor(string value)
    {
        try { return (Color)ColorConverter.ConvertFromString(value)!; }
        catch { throw new InvalidDataException($"Cor inválida: {value}. Use #RRGGBB ou #AARRGGBB."); }
    }

    private static IEnumerable<string> ColorValues(CustomThemeSettings t) =>
    [t.Background, t.Chrome, t.Surface, t.SurfaceAlt, t.SurfaceHover, t.Border,
     t.Focus, t.TextPrimary, t.TextSecondary, t.TextMuted, t.Accent, t.AccentAlt,
     t.AccentSurface, t.Success, t.Warning, t.Danger, t.BackgroundOverlay];

    private static double RelativeLuminance(Color color)
    {
        static double Channel(byte value)
        {
            var channel = value / 255d;
            return channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
}
