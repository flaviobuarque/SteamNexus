using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace SteamSwitcher.Views.Dialogs;

public partial class AddGameCoverDialog : Window
{
    private readonly AddGameCoverDialogViewModel _vm;

    public string? SelectedImagePath => _vm.IsValid ? _vm.CustomCoverPath : null;

    public AddGameCoverDialog(string gameName)
    {
        InitializeComponent();
        _vm = new AddGameCoverDialogViewModel(gameName);
        DataContext = _vm;
        Loaded += (_, _) =>
        {
            if (Owner is null) return;
            Width = Owner.ActualWidth;
            Height = Owner.ActualHeight;
            Left = Owner.Left;
            Top = Owner.Top;

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

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsValid) return;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}

public partial class AddGameCoverDialogViewModel : ObservableObject
{
    public string GameName { get; }

    [ObservableProperty] private string _customCoverPath = string.Empty;
    [ObservableProperty] private ImageSource? _previewImage;
    [ObservableProperty] private string _dimensionsText = string.Empty;
    [ObservableProperty] private string _ratioText = string.Empty;
    [ObservableProperty] private bool _isValid;
    [ObservableProperty] private string _validationText = string.Empty;

    private const double MinRatio = 0.60;
    private const double MaxRatio = 0.75;

    public AddGameCoverDialogViewModel(string gameName)
    {
        GameName = gameName;
    }

    [RelayCommand]
    private void BrowseCover()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecionar capa do jogo",
            Filter = "Imagens|*.jpg;*.jpeg;*.png;*.bmp;*.webp",
            Multiselect = false
        };

        if (dialog.ShowDialog() != true) return;
        CustomCoverPath = dialog.FileName;
        ValidateAndPreview();
    }

    [RelayCommand]
    private void RemoveCover()
    {
        CustomCoverPath = string.Empty;
        PreviewImage = null;
        DimensionsText = string.Empty;
        RatioText = string.Empty;
        IsValid = false;
        ValidationText = string.Empty;
    }

    private void ValidateAndPreview()
    {
        if (string.IsNullOrEmpty(CustomCoverPath) || !File.Exists(CustomCoverPath))
        {
            IsValid = false;
            ValidationText = "Arquivo não encontrado.";
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnDemand;
            bitmap.UriSource = new Uri(CustomCoverPath, UriKind.Absolute);
            bitmap.EndInit();

            var w = bitmap.PixelWidth;
            var h = bitmap.PixelHeight;

            if (w <= 0 || h <= 0)
            {
                IsValid = false;
                ValidationText = "Imagem inválida.";
                return;
            }

            var ratio = (double)w / h;

            DimensionsText = $"{w}×{h}";
            RatioText = $"{ratio:F3}";

            var valid = ratio >= MinRatio && ratio <= MaxRatio;
            IsValid = valid;
            ValidationText = valid
                ? "✓ Proporção aceita."
                : $"✗ Recusada. Proporção deve estar entre {MinRatio:F2} e {MaxRatio:F2} (≈ 2:3).";

            bitmap.Freeze();
            PreviewImage = bitmap;
        }
        catch
        {
            IsValid = false;
            ValidationText = "Não foi possível ler a imagem.";
        }
    }
}