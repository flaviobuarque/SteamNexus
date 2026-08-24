using System.Windows.Media.Animation;

namespace SteamSwitcher.Views.Controls;

public partial class BusyRing : System.Windows.Controls.UserControl
{
    public BusyRing()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        SpinnerRotation.BeginAnimation(
            System.Windows.Media.RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.85))
            {
                RepeatBehavior = RepeatBehavior.Forever
            });
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        SpinnerRotation.BeginAnimation(
            System.Windows.Media.RotateTransform.AngleProperty,
            null);
    }
}
