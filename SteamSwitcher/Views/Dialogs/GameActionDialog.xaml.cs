using SteamSwitcher.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SteamSwitcher.Views.Dialogs;

public partial class GameActionDialog : Window
{
    private readonly Func<Task<bool>> _operation;
    private readonly GamesViewModel _viewModel;
    private readonly CancellationTokenSource _countdownCancellation = new();
    private bool _countdownStarted;
    private bool _operationStarted;
    private bool _closed;
    private bool _busy;
    public bool Completed { get; private set; }

    public GameActionDialog(GamesViewModel viewModel, IReadOnlyList<GameCardViewModel> running,
        GameCardViewModel destination, string accountName, Func<Task<bool>> operation)
    {
        InitializeComponent();
        _operation = operation;
        _viewModel = viewModel;
        Heading.Text = running.Count > 0 ? "Trocar de jogo" : "Iniciar jogo";
        SourceGames.ItemsSource = running;
        SourceHeading.Visibility = WarningText.Visibility = running.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DestinationCover.Source = destination.CoverImage;
        DestinationName.Text = destination.Game.Name;
        DestinationAccount.Text = accountName;
        StatusText.SetBinding(TextBlock.TextProperty, new Binding(nameof(GamesViewModel.GameOperationMessage)) { Source = viewModel });
        Closing += (_, e) => { if (_busy) e.Cancel = true; };
        Closed += (_, _) =>
        {
            _closed = true;
            _countdownCancellation.Cancel();
            _countdownCancellation.Dispose();
            if (!_operationStarted)
                _viewModel.GameOperationMessage = "Abertura cancelada. Nenhum jogo foi encerrado e nenhuma conta foi trocada.";
        };
        Loaded += async (_, _) =>
        {
            if (Owner is not null)
            {
                Width = Owner.ActualWidth;
                Height = Owner.ActualHeight;
                Left = Owner.Left;
                Top = Owner.Top;
            }
            CancelButton.Focus();
            await StartCountdownAsync();
        };
    }

    private async Task StartCountdownAsync()
    {
        if (_countdownStarted || _closed) return;
        _countdownStarted = true;
        var ct = _countdownCancellation.Token;
        ContinueButton.IsEnabled = false;
        try
        {
            for (var seconds = 5; seconds > 0; seconds--)
            {
                _viewModel.GameOperationMessage = $"Iniciando em {seconds} segundo(s)… Você pode cancelar antes de qualquer alteração.";
                ContinueButton.Content = $"Aguarde {seconds}s";
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
            ct.ThrowIfCancellationRequested();
            if (!_closed) await ExecuteAsync();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private async Task ExecuteAsync()
    {
        if (_busy || _closed || _operationStarted) return;
        _operationStarted = true;
        _busy = true;
        _viewModel.GameOperationMessage = "Preparando a abertura. A contagem terminou; aguarde a conclusão desta etapa.";
        ContinueButton.IsEnabled = CancelButton.IsEnabled = false;
        ContinueButton.Visibility = CancelButton.Visibility = Visibility.Collapsed;
        ProgressRing.Visibility = Visibility.Visible;
        try { Completed = await _operation(); }
        catch (Exception ex) { _viewModel.GameOperationMessage = ex.Message; }
        finally
        {
            _busy = false;
            ProgressRing.Visibility = Visibility.Collapsed;
            ContinueButton.Visibility = Visibility.Collapsed;
            CancelButton.Content = "Fechar";
            CancelButton.IsEnabled = true;
            if (!Completed) CancelButton.Visibility = Visibility.Visible;
        }
        if (Completed) Close();
        else CancelButton.Focus();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (!_busy) Close();
    }
}
