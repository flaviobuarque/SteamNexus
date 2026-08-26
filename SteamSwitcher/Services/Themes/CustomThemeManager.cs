using SteamSwitcher.Core.Models;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;

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

        ApplyAccent(theme);

        var resources = new ResourceDictionary();
        var onAccent = ContrastRatio("#FFFFFF", theme.Accent) >= ContrastRatio("#0B1220", theme.Accent)
            ? Colors.White
            : ParseColor("#0B1220");
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
        resources["AppOnAccentTextBrush"] = FrozenBrush(onAccent);
        AddBrush(resources, "AppSuccessBrush", theme.Success);
        AddBrush(resources, "AppWarningBrush", theme.Warning);
        AddBrush(resources, "AppDangerBrush", theme.Danger);

        AddAliases(resources, theme);
        resources["AppCardCornerRadius"] = new CornerRadius(Clamp(theme.CardCornerRadius, 0, 28));
        resources["AppButtonCornerRadius"] = new CornerRadius(Clamp(theme.ButtonCornerRadius, 0, 20));
        resources["AppThemeShadowsEnabled"] = theme.ShadowsEnabled;
        resources["AppCardEffect"] = theme.ShadowsEnabled
            ? new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.22, Color = Colors.Black }
            : new BlurEffect { Radius = 0 };

        if (!string.IsNullOrWhiteSpace(theme.BackgroundImagePath)
            && File.Exists(theme.BackgroundImagePath))
            resources["AppBackgroundBrush"] = CreateBackgroundBrush(theme);

        _customResources = resources;
        Application.Current.Resources.MergedDictionaries.Add(resources);
    }

    public static void ApplyBaseAccent(AppTheme baseTheme) => ApplyAccent(
        baseTheme == AppTheme.Light
            ? CustomThemeSettings.CreateLight()
            : CustomThemeSettings.CreateDark());

    private static void ApplyAccent(CustomThemeSettings theme)
    {
        var accent = ParseColor(theme.Accent);
        Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(
            accent,
            accent,
            ParseColor(theme.AccentAlt),
            ParseColor(theme.AccentSurface));
    }

    public static async Task ExportAsync(CustomThemeSettings theme, string path, CancellationToken ct = default)
    {
        Validate(theme);
        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true);
        var portable = theme.Clone();
        if (!string.IsNullOrWhiteSpace(theme.BackgroundImagePath) && File.Exists(theme.BackgroundImagePath))
        {
            var extension = Path.GetExtension(theme.BackgroundImagePath).ToLowerInvariant();
            var assetName = "assets/background" + extension;
            portable.BackgroundImagePath = assetName;
            var asset = archive.CreateEntry(assetName, CompressionLevel.Optimal);
            await using var source = new FileStream(theme.BackgroundImagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            await using var destination = asset.Open();
            await source.CopyToAsync(destination, ct);
        }
        var manifest = archive.CreateEntry("theme.json", CompressionLevel.Optimal);
        await using var writer = new StreamWriter(manifest.Open());
        await writer.WriteAsync(JsonSerializer.Serialize(portable, JsonOptions).AsMemory(), ct);
    }

    public static async Task<CustomThemeSettings> ImportAsync(string path, CancellationToken ct = default)
    {
        if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            var legacyJson = await File.ReadAllTextAsync(path, ct);
            var legacyTheme = JsonSerializer.Deserialize<CustomThemeSettings>(legacyJson, JsonOptions)
                ?? throw new InvalidDataException("O arquivo de tema está vazio ou inválido.");
            Validate(legacyTheme);
            return legacyTheme;
        }

        using var archive = ZipFile.OpenRead(path);
        var manifest = archive.GetEntry("theme.json")
            ?? throw new InvalidDataException("O pacote não contém theme.json.");
        string json;
        await using (var stream = manifest.Open())
        using (var reader = new StreamReader(stream))
            json = await reader.ReadToEndAsync(ct);
        var theme = JsonSerializer.Deserialize<CustomThemeSettings>(json, JsonOptions)
            ?? throw new InvalidDataException("O arquivo de tema está vazio ou inválido.");
        if (!string.IsNullOrWhiteSpace(theme.BackgroundImagePath))
        {
            var asset = archive.GetEntry(theme.BackgroundImagePath.Replace('\\', '/'));
            if (asset is not null)
            {
                var extension = Path.GetExtension(asset.Name).ToLowerInvariant();
                var directory = ThemeAssetsDirectory();
                Directory.CreateDirectory(directory);
                var destination = Path.Combine(directory, $"background-{Guid.NewGuid():N}{extension}");
                await using var source = asset.Open();
                await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                await source.CopyToAsync(target, ct);
                theme.BackgroundImagePath = destination;
            }
            else theme.BackgroundImagePath = null;
        }
        Validate(theme);
        return theme;
    }

    public static string CopyBackgroundToLibrary(string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp"))
            throw new InvalidDataException("Use uma imagem PNG, JPG, WEBP ou BMP.");

        var directory = ThemeAssetsDirectory();
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, $"background-{Guid.NewGuid():N}{extension}");
        File.Copy(sourcePath, destination, false);
        return destination;
    }

    private static string ThemeAssetsDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamSwitcher", "Themes", "Assets");

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
        theme.BackgroundBlurRadius = Clamp(theme.BackgroundBlurRadius, 0, 24);
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
        var accent = ParseColor(theme.Accent);
        var accentAlt = ParseColor(theme.AccentAlt);
        var accentSurface = ParseColor(theme.AccentSurface);
        var onAccent = ContrastRatio("#FFFFFF", theme.Accent) >= ContrastRatio("#0B1220", theme.Accent)
            ? Colors.White
            : ParseColor("#0B1220");

        resources["SystemAccentColor"] = accent;
        resources["ApplicationAccentColor"] = accent;
        resources["SystemAccentColorPrimary"] = accent;
        resources["SystemAccentColorSecondary"] = accentAlt;
        resources["SystemAccentColorTertiary"] = accentSurface;
        resources["SystemAccentBrush"] = FrozenBrush(accent);
        resources["SystemAccentColorBrush"] = FrozenBrush(accent);
        resources["SystemAccentColorPrimaryBrush"] = FrozenBrush(accent);
        resources["SystemAccentColorSecondaryBrush"] = FrozenBrush(accentAlt);
        resources["SystemAccentColorTertiaryBrush"] = FrozenBrush(accentSurface);
        resources["PrimaryAccentBrush"] = FrozenBrush(accent);
        resources["SecondaryAccentBrush"] = FrozenBrush(accentAlt);
        resources["TertiaryAccentBrush"] = FrozenBrush(accentSurface);
        resources["AccentFillColorDefaultBrush"] = FrozenBrush(accent);
        resources["AccentFillColorSecondaryBrush"] = FrozenBrush(accentAlt);
        resources["AccentFillColorTertiaryBrush"] = FrozenBrush(accentSurface);
        resources["AccentFillColorDisabledBrush"] = FrozenBrush(accentSurface, 0.65);
        resources["AccentFillColorSelectedTextBackgroundBrush"] = FrozenBrush(accent);
        resources["AccentTextFillColorPrimaryBrush"] = FrozenBrush(accent);
        resources["AccentTextFillColorSecondaryBrush"] = FrozenBrush(accentAlt);
        resources["AccentTextFillColorTertiaryBrush"] = FrozenBrush(accentSurface);
        resources["AccentTextFillColorDisabledBrush"] = FrozenBrush(accentSurface, 0.65);
        resources["AccentButtonBorderBrush"] = FrozenBrush(accentAlt);
        resources["AccentControlElevationBorderBrush"] = FrozenBrush(accentAlt);
        resources["TextOnAccentFillColorPrimaryBrush"] = FrozenBrush(onAccent);
        resources["TextOnAccentFillColorSecondaryBrush"] = FrozenBrush(onAccent, 0.88);
        resources["TextOnAccentFillColorDisabledBrush"] = FrozenBrush(onAccent, 0.55);
        resources["TextOnAccentFillColorSelectedTextBrush"] = FrozenBrush(onAccent);
        resources["ControlStrokeColorOnAccentDefaultBrush"] = FrozenBrush(onAccent, 0.24);
        resources["ControlStrokeColorOnAccentSecondaryBrush"] = FrozenBrush(onAccent, 0.18);
        resources["ControlStrokeColorOnAccentTertiaryBrush"] = FrozenBrush(onAccent, 0.12);
        resources["ControlStrokeColorOnAccentDisabledBrush"] = FrozenBrush(onAccent, 0.08);
        resources["ControlStrongFillColorDefaultBrush"] = FrozenBrush(accent);
        resources["ControlStrongFillColorDisabledBrush"] = FrozenBrush(accentSurface, 0.65);
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

        ImageSource backgroundSource = bitmap;
        if (theme.BackgroundBlurRadius > 0.1)
            backgroundSource = CreateBlurredBitmap(bitmap, theme.BackgroundBlurRadius);

        var image = new ImageDrawing(backgroundSource, new Rect(0, 0, 1, 1));
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

    private static BitmapSource CreateBlurredBitmap(BitmapSource source, double radius)
    {
        const double maxDimension = 1920;
        var scale = Math.Min(1, maxDimension / Math.Max(source.PixelWidth, source.PixelHeight));
        var width = Math.Max(1, (int)Math.Round(source.PixelWidth * scale));
        var height = Math.Max(1, (int)Math.Round(source.PixelHeight * scale));
        var image = new System.Windows.Controls.Image
        {
            Source = source,
            Width = width,
            Height = height,
            Stretch = Stretch.UniformToFill,
            Effect = new BlurEffect
            {
                Radius = Clamp(radius, 0, 24),
                KernelType = KernelType.Gaussian,
            },
        };
        image.Measure(new Size(width, height));
        image.Arrange(new Rect(0, 0, width, height));
        var rendered = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(image);
        rendered.Freeze();
        return rendered;
    }

    private static void AddBrush(ResourceDictionary resources, string key, string value, double opacity = 1)
        => resources[key] = new SolidColorBrush(ParseColor(value)) { Opacity = Clamp(opacity, 0, 1) };

    private static SolidColorBrush FrozenBrush(Color color, double opacity = 1)
    {
        var brush = new SolidColorBrush(color) { Opacity = Clamp(opacity, 0, 1) };
        brush.Freeze();
        return brush;
    }

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
