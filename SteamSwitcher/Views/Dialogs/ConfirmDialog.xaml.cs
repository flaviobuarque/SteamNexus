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
            DialogIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xE3, 0x3B, 0x3B));
            ConfirmButton.Style = (Style)FindResource("DangerButtonStyle"); // veja abaixo
        }
        else
        {
            DialogIcon.Symbol = Symbol.QuestionCircle;
            DialogIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x6F, 0xEB));
        }

        Loaded += (_, _) =>
        {
            if (Owner != null)
            {
                Width = Owner.ActualWidth;
                Height = Owner.ActualHeight;
                Left = Owner.Left;
                Top = Owner.Top;
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