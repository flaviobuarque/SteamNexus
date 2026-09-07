using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using SteamSwitcher.Core.Services;

namespace SteamSwitcher.Views.Dialogs;

public partial class UpdateAwarenessDialog : Window
{
    public const string PolicyId = "remember-password-default-v1";
    private readonly IAppSettingsService _settings;
    private readonly Stopwatch _elapsed = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private bool _acknowledged;
    private bool _saving;

    public UpdateAwarenessDialog(IAppSettingsService settings)
    {
        _settings = settings;
        InitializeComponent();
        Loaded += (_, _) => { _elapsed.Start(); _timer.Start(); UpdateCountdown(); };
        _timer.Tick += (_, _) => UpdateCountdown();
        Closing += (_, e) => e.Cancel = !_acknowledged;
        Closed += (_, _) => _timer.Stop();
    }

    private void UpdateCountdown()
    {
        var remaining = Math.Max(0, 15 - (int)_elapsed.Elapsed.TotalSeconds);
        StatusText.Text = remaining > 0 ? $"Leia o aviso. Você poderá confirmar em {remaining}s."
            : "Clique em Estou ciente para continuar.";
        AcknowledgeButton.IsEnabled = remaining == 0 && !_saving;
        if (remaining == 0) _timer.Stop();
    }

    private async void Acknowledge_Click(object sender, RoutedEventArgs e)
    {
        if (_saving || _elapsed.Elapsed < TimeSpan.FromSeconds(15)) return;
        _saving = true;
        AcknowledgeButton.IsEnabled = false;
        var previous = _settings.Current.AcknowledgedAccountPolicy;
        try
        {
            _settings.Current.AcknowledgedAccountPolicy = PolicyId;
            await _settings.SaveAsync(_settings.Current);
            _acknowledged = true;
            DialogResult = true;
        }
        catch
        {
            _settings.Current.AcknowledgedAccountPolicy = previous;
            StatusText.Text = "Não foi possível salvar a confirmação. Clique para tentar novamente.";
            AcknowledgeButton.IsEnabled = true;
        }
        finally { _saving = false; }
    }
}
