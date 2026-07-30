using SteamSwitcher.ViewModels;
using System.Windows;

namespace SteamSwitcher.Views.Pages;

public partial class ModsPage : System.Windows.Controls.Page,
    Wpf.Ui.Abstractions.Controls.INavigableView<ModsViewModel>
{
    public ModsViewModel ViewModel { get; }

    public ModsPage(ModsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += Page_Loaded;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
    }
}
