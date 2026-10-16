// ConfirmDialog.xaml.cs
using FluentIcons.Common;
using System.Windows;
using System.Windows.Media;

namespace SteamSwitcher.Views.Dialogs;

public partial class ConfirmDialog : Window
{
    public bool Confirmed { get; private set; }

    public enum DialogKind { Question, Danger }

    public ConfirmDialog(
        string title,
        string message,
        string confirmText = "Confirmar",
        string cancelText = "Cancelar",
        DialogKind kind = DialogKind.Question)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = cancelText;

        if (kind == DialogKind.Danger)
        {
            DialogIcon.Symbol = Symbol.Warning;
            DialogIcon.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "AppDangerBrush");
            ConfirmButton.Style = (Style)FindResource("NexusDangerButton");
        }
        else
        {
            DialogIcon.Symbol = Symbol.QuestionCircle;
            DialogIcon.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "AppAccentBrush");
        }

        Loaded += (_, _) =>
        {
            if (Owner != null)
            {
                Width = Owner.ActualWidth;
                Height = Owner.ActualHeight;
                Left = Owner.Left;
                Top = Owner.Top;
                DialogCard.MaxHeight = Math.Max(180, Owner.ActualHeight - 32);
                DialogCard.Width = Math.Min(440, Math.Max(280, Owner.ActualWidth - 32));
            }
        };
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }
}
