using CommunityToolkit.Mvvm.ComponentModel;
using SteamSwitcher.Core.Models;
using System.Windows.Media;

namespace SteamSwitcher.ViewModels;

public partial class GameCardViewModel : ObservableObject
{
    public SteamGame Game { get; }

    [ObservableProperty] private string _coverPath = string.Empty;
    [ObservableProperty] private ImageSource? _coverImage;
    [ObservableProperty] private bool _isLaunching;
    [ObservableProperty] private bool _coverMissing;
    [ObservableProperty] private bool _isVisibleInViewport;
    [ObservableProperty] private string _heroCoverPath = string.Empty;
    [ObservableProperty] private bool _isRunning;

    private bool _loadRequested;
    public event EventHandler? LoadRequested;
    public bool HasLoadBeenRequested => _loadRequested;

    public GameCardViewModel(SteamGame game)
    {
        Game = game;
    }

    public string OwnerAccountName =>
        Game.OwnerAccount?.AccountName ?? string.Empty;

    public bool HasOwner => Game.OwnerAccount is not null;

    public string SizeAndDrive => Game.SizeAndDrive;

    public void OnOwnerChanged()
    {
        OnPropertyChanged(nameof(HasOwner));
        OnPropertyChanged(nameof(OwnerAccountName));
    }

    partial void OnIsVisibleInViewportChanged(bool value)
    {
        if (value && !_loadRequested)
            LoadRequested?.Invoke(this, EventArgs.Empty);
    }

    public void MarkLoadRequested() => _loadRequested = true;

    public void ResetLoadState()
    {
        _loadRequested = false;

        if (IsVisibleInViewport)
            LoadRequested?.Invoke(this, EventArgs.Empty);
    }
}