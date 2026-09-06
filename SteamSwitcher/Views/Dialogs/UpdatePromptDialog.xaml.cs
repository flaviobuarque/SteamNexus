using System.Windows;

namespace SteamSwitcher.Views.Dialogs;

public partial class UpdatePromptDialog : Window
{
    public enum UpdateChoice
    {
        Later,
        DownloadOnly,
        DownloadAndInstall
    }

    public UpdateChoice Choice { get; private set; } = UpdateChoice.Later;

    public UpdatePromptDialog(string version, string releaseNotes, bool isReady)
    {
        InitializeComponent();
        VersionText.Text = $"Versão {version}";
        ReleaseNotesText.Text = releaseNotes;
        DescriptionText.Text = isReady
            ? "A atualização já foi baixada e está pronta para ser instalada. O aplicativo será reiniciado."
            : "Escolha se deseja instalar assim que o download terminar, apenas preparar a atualização ou adiar.";

        if (isReady)
        {
            InstallButtonText.Text = "Instalar e reiniciar agora";
            DownloadButton.Visibility = Visibility.Collapsed;
        }

        Loaded += (_, _) =>
        {
            if (Owner is null) return;
            Width = Owner.ActualWidth;
            Height = Owner.ActualHeight;
            Left = Owner.Left;
            Top = Owner.Top;
        };
    }

    private void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = UpdateChoice.DownloadAndInstall;
        DialogResult = true;
    }

    private void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = UpdateChoice.DownloadOnly;
        DialogResult = true;
    }

    private void LaterButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = UpdateChoice.Later;
        DialogResult = false;
    }
}
