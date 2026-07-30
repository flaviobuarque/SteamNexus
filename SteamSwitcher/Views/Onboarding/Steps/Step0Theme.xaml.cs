using SteamSwitcher.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;

namespace SteamSwitcher.Views.Onboarding.Steps;

public partial class Step0Theme : UserControl
{
    public Step0Theme() => InitializeComponent();

    private void CardDark_Click(object sender, MouseButtonEventArgs e)
        => Apply("Dark");

    private void CardLight_Click(object sender, MouseButtonEventArgs e)
        => Apply("Light");

    private void CardSystem_Click(object sender, MouseButtonEventArgs e)
        => Apply("System");

    private void Apply(string theme)
    {
        if (DataContext is not OnboardingViewModel vm) return;
        vm.SelectThemeCommand.Execute(theme);
    }
}