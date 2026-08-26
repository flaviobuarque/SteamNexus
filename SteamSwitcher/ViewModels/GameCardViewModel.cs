using CommunityToolkit.Mvvm.ComponentModel;
using SteamSwitcher.Core.Models;
using System.Windows.Media;

namespace SteamSwitcher.ViewModels;

public partial class GameCardViewModel : ObservableObject
{
    public SteamGame Game { get; private set; }

    [ObservableProperty] private string _coverPath = string.Empty;
    [ObservableProperty] private ImageSource? _coverImage;
    [ObservableProperty] private bool _isLaunching;
    [ObservableProperty] private bool _coverMissing;
    [ObservableProperty] private bool _isCoverLoading;
    [ObservableProperty] private bool _isFavorite;

    public GameCardViewModel(SteamGame game, bool isFavorite = false)
    {
        Game = game;
        IsFavorite = isFavorite;
    }

    public string OwnerAccountName =>
        Game.OwnerAccount?.AccountName ?? string.Empty;

    public bool HasOwner => Game.OwnerAccount is not null;

    public string SizeAndDrive => Game.SizeAndDrive;
    public string InstallDrive => Game.DriveLetter.ToUpperInvariant();
    public string InstallSize => Game.SizeFormatted;
    public string InstallFullPath => Game.InstallFullPath;
    public string InstallationName => Game.InstallationName;
    public string InstallationRootPath => Game.InstallationRootPath;

    public void ApplySnapshot(SteamGame game)
    {
        Game = game;
        OnPropertyChanged(nameof(Game));
        OnPropertyChanged(nameof(OwnerAccountName));
        OnPropertyChanged(nameof(HasOwner));
        OnPropertyChanged(nameof(SizeAndDrive));
        OnPropertyChanged(nameof(InstallDrive));
        OnPropertyChanged(nameof(InstallSize));
        OnPropertyChanged(nameof(InstallFullPath));
        OnPropertyChanged(nameof(InstallationName));
        OnPropertyChanged(nameof(InstallationRootPath));
    }

    public void OnOwnerChanged()
    {
        OnPropertyChanged(nameof(HasOwner));
        OnPropertyChanged(nameof(OwnerAccountName));
    }
}
