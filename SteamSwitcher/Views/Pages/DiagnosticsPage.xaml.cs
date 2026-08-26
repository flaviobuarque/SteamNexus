using SteamSwitcher.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace SteamSwitcher.Views.Pages;

public partial class DiagnosticsPage : Page,
    Wpf.Ui.Abstractions.Controls.INavigableView<DiagnosticsViewModel>
{
    public DiagnosticsViewModel ViewModel { get; }

    public DiagnosticsPage(DiagnosticsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) =>
        await ViewModel.InitializeAsync();
}
