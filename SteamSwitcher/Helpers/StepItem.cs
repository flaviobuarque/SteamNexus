using CommunityToolkit.Mvvm.ComponentModel;

namespace SteamSwitcher.Helpers;

public partial class StepItem(int number, string label) : ObservableObject
{
    public int Number { get; } = number;
    public string Label { get; } = label;

    [ObservableProperty] private bool _isCurrent;
    [ObservableProperty] private bool _isCompleted;
}