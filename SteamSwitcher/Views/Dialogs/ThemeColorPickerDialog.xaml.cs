using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SteamSwitcher.Views.Dialogs;

public partial class ThemeColorPickerDialog : Window
{
    private static readonly List<string> SavedColors = ["#EF4444", "#F97316", "#FBBF24", "#22C55E", "#14B8A6", "#3B82F6", "#8B5CF6", "#D946EF"];
    private static readonly string[] PaletteColors =
    [
        "#FCA5A5", "#FDBA74", "#FDE047", "#86EFAC", "#5EEAD4", "#7DD3FC", "#93C5FD", "#C4B5FD", "#E9D5FF", "#F0ABFC",
        "#EF4444", "#F97316", "#EAB308", "#22C55E", "#14B8A6", "#0EA5E9", "#3B82F6", "#8B5CF6", "#A855F7", "#D946EF",
        "#B91C1C", "#C2410C", "#A16207", "#15803D", "#0F766E", "#0369A1", "#1D4ED8", "#6D28D9", "#7E22CE", "#A21CAF",
        "#FFFFFF", "#E2E8F0", "#CBD5E1", "#94A3B8", "#64748B", "#475569", "#334155", "#1E293B", "#0F172A", "#000000",
    ];

    private bool _updating;
    private double _hue;
    private double _saturation;
    private double _value;
    private double _alpha = 1;

    public string SelectedColor { get; private set; }

    public ThemeColorPickerDialog(string initialColor)
    {
        InitializeComponent();
        SelectedColor = Normalize(initialColor) ?? "#000000";
        SetFromColor((Color)ColorConverter.ConvertFromString(SelectedColor)!);
        BuildPalette();
        BuildSavedColors();
        Loaded += (_, _) => UpdateVisuals();
        SizeChanged += (_, _) => UpdateSelectorPositions();
    }

    private void ColorArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => UpdateSaturationValue(e.GetPosition(ColorArea));
    private void ColorArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) UpdateSaturationValue(e.GetPosition(ColorArea));
    }
    private void HueBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => UpdateHue(e.GetPosition(HueBar).X);
    private void HueBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) UpdateHue(e.GetPosition(HueBar).X);
    }
    private void AlphaBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => UpdateAlpha(e.GetPosition(AlphaBar).X);
    private void AlphaBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) UpdateAlpha(e.GetPosition(AlphaBar).X);
    }

    private void UpdateSaturationValue(Point point)
    {
        _saturation = Clamp(point.X / Math.Max(1, ColorArea.ActualWidth));
        _value = 1 - Clamp(point.Y / Math.Max(1, ColorArea.ActualHeight));
        UpdateFromHsv();
    }

    private void UpdateHue(double x) { _hue = Clamp(x / Math.Max(1, HueBar.ActualWidth)) * 360; UpdateFromHsv(); }
    private void UpdateAlpha(double x) { _alpha = Clamp(x / Math.Max(1, AlphaBar.ActualWidth)); UpdateFromHsv(); }

    private void UpdateFromHsv()
    {
        var rgb = HsvToColor(_hue, _saturation, _value);
        var color = Color.FromArgb((byte)Math.Round(_alpha * 255), rgb.R, rgb.G, rgb.B);
        SelectedColor = ToHex(color);
        _updating = true;
        HexTextBox.Text = SelectedColor;
        _updating = false;
        ValidationText.Text = string.Empty;
        UpdateVisuals();
    }

    private void HexTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating || PreviewBorder is null) return;
        var normalized = Normalize(HexTextBox.Text);
        if (normalized is null) { ValidationText.Text = "Informe #RRGGBB ou #AARRGGBB."; return; }
        ValidationText.Text = string.Empty;
        SelectedColor = normalized;
        SetFromColor((Color)ColorConverter.ConvertFromString(normalized)!);
    }

    private void SetFromColor(Color color)
    {
        _alpha = color.A / 255d;
        RgbToHsv(color, out _hue, out _saturation, out _value);
        _updating = true;
        if (HexTextBox is not null) HexTextBox.Text = ToHex(color);
        _updating = false;
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (PreviewBorder is null) return;
        var hueColor = HsvToColor(_hue, 1, 1);
        var rgb = HsvToColor(_hue, _saturation, _value);
        var color = Color.FromArgb((byte)Math.Round(_alpha * 255), rgb.R, rgb.G, rgb.B);
        HueGradientStop.Color = hueColor;
        AlphaTransparentStop.Color = Color.FromArgb(0, rgb.R, rgb.G, rgb.B);
        AlphaOpaqueStop.Color = Color.FromArgb(255, rgb.R, rgb.G, rgb.B);
        PreviewBorder.Background = new SolidColorBrush(color);
        AlphaText.Text = $"{_alpha:P0}";
        UpdateSelectorPositions();
    }

    private void UpdateSelectorPositions()
    {
        if (!IsLoaded) return;
        Canvas.SetLeft(ColorSelector, _saturation * ColorArea.ActualWidth - ColorSelector.Width / 2);
        Canvas.SetTop(ColorSelector, (1 - _value) * ColorArea.ActualHeight - ColorSelector.Height / 2);
        Canvas.SetLeft(HueSelector, _hue / 360 * HueBar.ActualWidth - HueSelector.Width / 2);
        Canvas.SetLeft(AlphaSelector, _alpha * AlphaBar.ActualWidth - AlphaSelector.Width / 2);
    }

    private void BuildPalette()
    {
        foreach (var color in PaletteColors) PalettePanel.Children.Add(CreateColorButton(color, 30));
    }

    private void BuildSavedColors()
    {
        SavedColorsPanel.Children.Clear();
        foreach (var color in SavedColors) SavedColorsPanel.Children.Add(CreateColorButton(color, 28));
    }

    private Button CreateColorButton(string color, double size)
    {
        var button = new Button
        {
            Width = size, Height = size, Margin = new Thickness(3), Padding = new Thickness(0),
            BorderThickness = new Thickness(1), BorderBrush = (Brush)FindResource("AppBorderBrush"),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!), Tag = color, Cursor = Cursors.Hand,
        };
        button.Click += PaletteColor_Click;
        return button;
    }

    private void PaletteColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string color }) return;
        SelectedColor = color;
        SetFromColor((Color)ColorConverter.ConvertFromString(color)!);
    }

    private void AddSavedColor_Click(object sender, RoutedEventArgs e)
    {
        var rgb = HsvToColor(_hue, _saturation, _value);
        var opaque = $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";
        SavedColors.Remove(opaque);
        SavedColors.Insert(0, opaque);
        if (SavedColors.Count > 16) SavedColors.RemoveAt(SavedColors.Count - 1);
        BuildSavedColors();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (Normalize(HexTextBox.Text) is null) { ValidationText.Text = "Corrija a cor antes de continuar."; return; }
        DialogResult = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (!text.StartsWith('#')) text = "#" + text;
        if (text.Length is not (7 or 9)) return null;
        try { return ToHex((Color)ColorConverter.ConvertFromString(text)!); }
        catch { return null; }
    }

    private static string ToHex(Color color) => color.A == 255 ? $"#{color.R:X2}{color.G:X2}{color.B:X2}" : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private static Color HsvToColor(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var h = (hue % 360) / 60;
        var x = chroma * (1 - Math.Abs(h % 2 - 1));
        var (r, g, b) = h switch { < 1 => (chroma, x, 0d), < 2 => (x, chroma, 0d), < 3 => (0d, chroma, x), < 4 => (0d, x, chroma), < 5 => (x, 0d, chroma), _ => (chroma, 0d, x) };
        var m = value - chroma;
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private static void RgbToHsv(Color color, out double hue, out double saturation, out double value)
    {
        var r = color.R / 255d; var g = color.G / 255d; var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var delta = max - min;
        hue = delta == 0 ? 0 : max == r ? 60 * (((g - b) / delta) % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        if (hue < 0) hue += 360;
        saturation = max == 0 ? 0 : delta / max;
        value = max;
    }

    private static double Clamp(double value) => Math.Max(0, Math.Min(1, value));
}
