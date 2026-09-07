using SteamSwitcher.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SteamSwitcher.Views.Dialogs;

public partial class GameActionDialog : Window
{
    private readonly Func<Task<bool>> _operation;
    private readonly GamesViewModel _viewModel;
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
            if (running.Count == 0) await ExecuteAsync();
        };
    }

    private async void Continue_Click(object sender, RoutedEventArgs e) => await ExecuteAsync();

    private async Task ExecuteAsync()
    {
        if (_busy) return;
        _busy = true;
        ContinueButton.IsEnabled = CancelButton.IsEnabled = false;
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
        }
        if (Completed) Close();
        else CancelButton.Focus();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (!_busy) Close();
    }
}
