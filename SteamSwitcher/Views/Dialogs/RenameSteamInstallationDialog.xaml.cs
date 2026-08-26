using System.Windows;

namespace SteamSwitcher.Views.Dialogs;

public partial class RenameSteamInstallationDialog : Window
{
    public string InstallationName => NameBox.Text.Trim();

    public RenameSteamInstallationDialog(string name, string path)
    {
        InitializeComponent();
        NameBox.Text = name;
        PathText.Text = path;
        Loaded += (_, _) =>
        {
            if (Owner is not null)
            {
                Width = Owner.ActualWidth;
                Height = Owner.ActualHeight;
                Left = Owner.Left;
                Top = Owner.Top;
            }
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
