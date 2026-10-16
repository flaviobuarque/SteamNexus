using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using FluentIcons.Common;
using FluentIcons.Wpf;
using SteamSwitcher.Services.Updates;

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

        if (Owner is null)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        VersionText.Text = $"Versão instalada: {currentVersion}   •   Nova versão: {version}";
        DescriptionText.Text = isReady
            ? "A atualização já foi baixada e está pronta para ser instalada. O aplicativo será reiniciado."
            : "Escolha se deseja instalar assim que o download terminar, apenas preparar a atualização ou adiar.";

        if (isReady)
        {
            InstallButtonText.Text = "Instalar e reiniciar agora";
            DownloadButton.Visibility = Visibility.Collapsed;
        }

        PopulateReleaseNotes(releaseNotes);

        Loaded += (_, _) =>
        {
            if (Owner is null)
            {
                DialogCard.Width = 620;
                DialogCard.Height = 520;
                return;
            }

            Width = Owner.ActualWidth;
            Height = Owner.ActualHeight;
            DialogCard.Width = Math.Min(620, Math.Max(320, Width - 24));
            DialogCard.Height = Math.Min(620, Math.Max(360, Height - 24));
            Left = Owner.Left;
            Top = Owner.Top;
        };
    }

    private void PopulateReleaseNotes(string rawNotes)
    {
        ReleaseNotesViewer.Document.Blocks.Clear();
        var formatted = ReleaseNotesFormatter.Format(rawNotes);
        var lines = formatted.Split('\n');
        var isFirstHeader = true;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith('#'))
            {
                var categoryText = line.TrimStart('#', ' ').Trim();
                var symbol = Symbol.Sparkle;
                var iconBrushKey = "AppAccentBrush";

                if (categoryText.Contains("Melhoria", StringComparison.OrdinalIgnoreCase) ||
                    categoryText.Contains("Desempenho", StringComparison.OrdinalIgnoreCase))
                {
                    symbol = Symbol.ArrowTrending;
                    iconBrushKey = "AppAccentAltBrush";
                }
                else if (categoryText.Contains("Correç", StringComparison.OrdinalIgnoreCase) ||
                         categoryText.Contains("Correc", StringComparison.OrdinalIgnoreCase) ||
                         categoryText.Contains("Bug", StringComparison.OrdinalIgnoreCase) ||
                         categoryText.Contains("Ajuste", StringComparison.OrdinalIgnoreCase))
                {
                    symbol = Symbol.Wrench;
                    iconBrushKey = "AppAccentBrush";
                }

                var headerParagraph = new Paragraph
                {
                    Margin = new Thickness(0, isFirstHeader ? 2 : 16, 0, 7),
                    KeepWithNext = true
                };
                isFirstHeader = false;

                var icon = new SymbolIcon
                {
                    Symbol = symbol,
                    FontSize = 14,
                    Margin = new Thickness(0, 0, 7, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                icon.SetResourceReference(SymbolIcon.ForegroundProperty, iconBrushKey);

                var iconContainer = new InlineUIContainer(icon)
                {
                    BaselineAlignment = BaselineAlignment.Center
                };
                headerParagraph.Inlines.Add(iconContainer);

                var titleRun = new Run(categoryText)
                {
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 13.5
                };
                titleRun.SetResourceReference(TextElement.ForegroundProperty, "AppTextPrimaryBrush");
                headerParagraph.Inlines.Add(titleRun);

                ReleaseNotesViewer.Document.Blocks.Add(headerParagraph);
            }
            else
            {
                var itemText = line;
                if (itemText.StartsWith('-') || itemText.StartsWith('*'))
                    itemText = itemText[1..].Trim();

                itemText = Regex.Replace(itemText, @"\[([^\]]+)\]\([^)]+\)", "$1").Replace("`", "");

                var itemParagraph = new Paragraph
                {
                    Margin = new Thickness(4, 2, 0, 4),
                    LineHeight = 19
                };

                var bulletRun = new Run("• ")
                {
                    FontWeight = FontWeights.Bold
                };
                bulletRun.SetResourceReference(TextElement.ForegroundProperty, "AppAccentBrush");
                itemParagraph.Inlines.Add(bulletRun);

                var boldMatch = Regex.Match(itemText, @"^\*\*(.*?)\*\*(.*)$");
                if (boldMatch.Success)
                {
                    var boldText = boldMatch.Groups[1].Value.Trim();
                    var normalText = boldMatch.Groups[2].Value.TrimStart();

                    var boldRun = new Run(boldText + " ")
                    {
                        FontWeight = FontWeights.SemiBold
                    };
                    boldRun.SetResourceReference(TextElement.ForegroundProperty, "AppTextPrimaryBrush");
                    itemParagraph.Inlines.Add(boldRun);

                    if (!string.IsNullOrWhiteSpace(normalText))
                    {
                        var regularRun = new Run(normalText)
                        {
                            FontWeight = FontWeights.Normal
                        };
                        regularRun.SetResourceReference(TextElement.ForegroundProperty, "AppTextSecondaryBrush");
                        itemParagraph.Inlines.Add(regularRun);
                    }
                }
                else
                {
                    var cleanText = itemText.Replace("**", "");
                    var regularRun = new Run(cleanText)
                    {
                        FontWeight = FontWeights.Normal
                    };
                    regularRun.SetResourceReference(TextElement.ForegroundProperty, "AppTextPrimaryBrush");
                    itemParagraph.Inlines.Add(regularRun);
                }

                ReleaseNotesViewer.Document.Blocks.Add(itemParagraph);
            }
        }

        if (ReleaseNotesViewer.Document.Blocks.Count == 0)
        {
            var fallback = new Paragraph(new Run("As novidades desta versão ainda não foram publicadas."))
            {
                Margin = new Thickness(0, 4, 0, 4)
            };
            fallback.SetResourceReference(TextElement.ForegroundProperty, "AppTextSecondaryBrush");
            ReleaseNotesViewer.Document.Blocks.Add(fallback);
        }
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
