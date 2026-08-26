using System.Windows;
using System.Windows.Media;

namespace SteamSwitcher.Views.Dialogs;

public partial class ThemeColorPickerDialog : Window
{
    private bool _updating;
    public string SelectedColor { get; private set; }

    public ThemeColorPickerDialog(string initialColor)
    {
        InitializeComponent();
        SelectedColor = Normalize(initialColor) ?? "#000000";
        var color = (Color)ColorConverter.ConvertFromString(SelectedColor)!;
        _updating = true;
        RedSlider.Value = color.R;
        GreenSlider.Value = color.G;
        BlueSlider.Value = color.B;
        HexTextBox.Text = SelectedColor;
        _updating = false;
        UpdatePreview(color);
    }

    private void RgbSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || PreviewBorder is null) return;
        var color = Color.FromRgb((byte)RedSlider.Value, (byte)GreenSlider.Value, (byte)BlueSlider.Value);
        SelectedColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        _updating = true;
        HexTextBox.Text = SelectedColor;
        _updating = false;
        ValidationText.Text = string.Empty;
        UpdatePreview(color);
    }

    private void HexTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_updating || PreviewBorder is null) return;
        var normalized = Normalize(HexTextBox.Text);
        if (normalized is null)
        {
            ValidationText.Text = "Informe uma cor no formato #RRGGBB.";
            return;
        }

        var color = (Color)ColorConverter.ConvertFromString(normalized)!;
        SelectedColor = normalized;
        _updating = true;
        RedSlider.Value = color.R;
        GreenSlider.Value = color.G;
        BlueSlider.Value = color.B;
        _updating = false;
        ValidationText.Text = string.Empty;
        UpdatePreview(color);
    }

    private void UpdatePreview(Color color) => PreviewBorder.Background = new SolidColorBrush(color);

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (Normalize(HexTextBox.Text) is null)
        {
            ValidationText.Text = "Corrija a cor antes de continuar.";
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (!text.StartsWith('#')) text = "#" + text;
        if (text.Length != 7) return null;
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(text)!;
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        catch { return null; }
    }
}
