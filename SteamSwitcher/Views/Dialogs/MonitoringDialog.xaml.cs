using SteamSwitcher.Core.Helpers;
using System.Windows;

namespace SteamSwitcher.Views.Dialogs;

public partial class MonitoringDialog : Window
{
    public MonitoringDialog()
    {
        InitializeComponent();
        Helpers.DialogChrome.Apply(this);
        Metrics.Text = RuntimeMonitorMetrics.Shared.Summary;
    }
    private void Refresh_Click(object sender, RoutedEventArgs e)
        => Metrics.Text = RuntimeMonitorMetrics.Shared.Summary;
}
