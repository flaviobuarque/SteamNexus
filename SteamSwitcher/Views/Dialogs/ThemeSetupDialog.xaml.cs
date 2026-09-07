using SteamSwitcher.Core.Models;
using SteamSwitcher.Services.Themes;
using System.Windows;
using System.Windows.Media;

namespace SteamSwitcher.Views.Dialogs;

public partial class ThemeSetupDialog : Window
{
    public CustomThemeSettings? ResultTheme { get; private set; }
    public string ThemeName => NameBox.Text.Trim();
    public ThemeSetupDialog(CustomThemeSettings current, bool rename = false)
    {
        InitializeComponent();
        var copy = current.Clone();
        copy.Name = "Tema atual";
        BaseChoice.ItemsSource = new[] { copy }.Concat(CustomThemeManager.BuiltInPresetNames.Select(CustomThemeManager.CreateBuiltInPreset)).ToArray();
        BaseChoice.SelectedIndex = 0;
        NameBox.Text = rename ? current.Name : "Meu tema";
        if (rename)
        {
            Heading.Text = "Renomear tema";
            BasePanel.Visibility = Visibility.Collapsed;
            Hint.Text = "A aparência e a identidade do tema serão preservadas.";
            ContinueButton.Content = "Renomear";
        }
    }
    private void Base_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (BaseChoice.SelectedItem is not CustomThemeSettings theme || Sample is null) return;
        var converter = new BrushConverter();
        Sample.Background = (Brush)converter.ConvertFromString(theme.Surface)!;
        AccentSample.Background = (Brush)converter.ConvertFromString(theme.Accent)!;
        SampleText.Foreground = (Brush)converter.ConvertFromString(theme.TextPrimary)!;
    }
    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ThemeName)) { Hint.Text = "Informe um nome para continuar."; NameBox.Focus(); return; }
        ResultTheme = ((CustomThemeSettings)BaseChoice.SelectedItem).Clone();
        ResultTheme.Name = ThemeName;
        ResultTheme.IsEnabled = true;
        DialogResult = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
