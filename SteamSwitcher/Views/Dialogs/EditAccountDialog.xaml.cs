using SteamSwitcher.Core.Models;
using SteamSwitcher.ViewModels;
using System.Windows;
using System.Windows.Media.Animation;

namespace SteamSwitcher.Views.Dialogs;

public partial class EditAccountDialog : Window
{
    private readonly EditAccountViewModel _viewModel;

    public EditAccountDialog(EditAccountViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = _viewModel;

        Loaded += (_, _) =>
        {
            if (Owner is null) return;
            Width = Owner.ActualWidth;
            Height = Owner.ActualHeight;
            Left = Owner.Left;
            Top = Owner.Top;

            // Animação: escala de 0.85 para 1.0 + fade in
            DialogCard.RenderTransformOrigin = new Point(0.5, 0.5);
            var scaleX = new DoubleAnimation(0.85, 1.0,
                new Duration(TimeSpan.FromMilliseconds(180)))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var scaleY = new DoubleAnimation(0.85, 1.0,
                new Duration(TimeSpan.FromMilliseconds(180)))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var fade = new DoubleAnimation(0, 1,
                new Duration(TimeSpan.FromMilliseconds(150)));

            var transform = (System.Windows.Media.ScaleTransform)DialogCard.RenderTransform;
            transform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleX);
            transform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleY);
            DialogCard.BeginAnimation(OpacityProperty, fade);
        };
    }

    public void LoadAccount(SteamAccount account, string steamAvatarPath) =>
    _viewModel.Load(account, steamAvatarPath);

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.SaveAsync();
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

public class LoginStateItem
{
    public int Value { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Color { get; set; } = "#EAF7FF";
}