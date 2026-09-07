using System.Windows;
using System.Windows.Documents;
using System.Text.RegularExpressions;

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

    public UpdatePromptDialog(string currentVersion, string version, string releaseNotes, bool isReady)
    {
        InitializeComponent();
        VersionText.Text = $"Versão instalada: {currentVersion}   •   Nova versão: {version}";
        foreach (var line in releaseNotes.Replace("\r", "").Split('\n'))
        {
            var heading = line.StartsWith('#');
            var text = Regex.Replace(line.TrimStart('#').Trim(), @"\[([^\]]+)\]\([^)]+\)", "$1")
                .Replace("**", "").Replace("`", "");
            if (text.StartsWith("- ")) text = "• " + text[2..];
            var paragraph = new Paragraph(new Run(text)) { Margin = new Thickness(0, 0, 0, 8) };
            if (heading) paragraph.FontWeight = FontWeights.SemiBold;
            ReleaseNotesViewer.Document.Blocks.Add(paragraph);
        }
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
            DialogCard.MaxHeight = Math.Max(250, Height - 24);
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
