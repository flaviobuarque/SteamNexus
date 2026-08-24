namespace SteamSwitcher.Views.Controls;

public partial class SkeletonCard : System.Windows.Controls.UserControl
{
    public SkeletonCard()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        var animation = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = -300,
            To = 300,
            Duration = TimeSpan.FromSeconds(1.4),
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
        };

        ShimmerTranslate.BeginAnimation(
            System.Windows.Media.TranslateTransform.XProperty,
            animation);
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        ShimmerTranslate.BeginAnimation(
            System.Windows.Media.TranslateTransform.XProperty,
            null);
    }
}
