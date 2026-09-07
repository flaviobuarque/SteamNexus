using CommunityToolkit.Mvvm.ComponentModel;
using SteamSwitcher.Core.Models;
using System.Windows.Media;

namespace SteamSwitcher.ViewModels;

public partial class GameCardViewModel : ObservableObject
{
    public SteamGame Game { get; private set; }
    [ObservableProperty] private ImageSource? _trayIconImage;
    public string? RuntimeIconPath { get; set; }
    private LoginState? _globalLoginPreference;
    public string LoginPreferenceText => Helpers.LoginPreferencePresentation.Label(Game.LoginStateOverride,
        Game.OwnerAccount?.LoginStateOverride ?? _globalLoginPreference);
    public LoginState? LoginPreferenceValue => Game.LoginStateOverride;
    public string LoginPreferenceInheritedText => $"{(Game.OwnerAccount?.LoginStateOverride.HasValue == true ? "Da conta" : "Padrão global")}: {Helpers.LoginPreferencePresentation.StateName(Game.OwnerAccount?.LoginStateOverride ?? _globalLoginPreference)}";
    public string LoginPreferenceTooltip => $"{(Game.LoginStateOverride.HasValue ? LoginPreferenceText : LoginPreferenceInheritedText)} · Clique para alterar";
    public void RefreshLoginPreference(LoginState? globalPreference)
    {
        _globalLoginPreference = globalPreference;
        OnPropertyChanged(nameof(LoginPreferenceText));
        OnPropertyChanged(nameof(LoginPreferenceTooltip));
        OnPropertyChanged(nameof(LoginPreferenceValue));
        OnPropertyChanged(nameof(LoginPreferenceInheritedText));
    }

    [ObservableProperty] private string _coverPath = string.Empty;
    [ObservableProperty] private ImageSource? _coverImage;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(RuntimeStatus), nameof(PrimaryActionText))]
    private bool _isLaunching;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RuntimeStatus), nameof(PrimaryActionText))]
    private bool _isRunning;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(RuntimeStatus), nameof(PrimaryActionText))]
    private bool _isStopping;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(RuntimeStatus), nameof(PrimaryActionText))]
    private bool _isUninstalling;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RuntimeStatus))]
    private bool _runtimeUncertain;
    [ObservableProperty] private string _runtimeAccountName = "Conta não identificada";
    public bool IsBusy => IsLaunching || IsStopping || IsUninstalling;
    public string RuntimeStatus => IsStopping ? "Encerrando…" : IsUninstalling ? "Aguardando desinstalação…"
        : IsLaunching ? "Iniciando…" : RuntimeUncertain ? "Execução não confirmada" : IsRunning ? "● Em execução" : string.Empty;
    public string PrimaryActionText => IsBusy ? RuntimeStatus : IsRunning ? "Parar" : HasOwner ? "Jogar" : "Associar conta";
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
        game.LastPlayedTimestamp = Math.Max(game.LastPlayedTimestamp, Game.LastPlayedTimestamp);
        Game = game;
        OnPropertyChanged(nameof(Game));
        OnPropertyChanged(nameof(OwnerAccountName));
        OnPropertyChanged(nameof(HasOwner));
        OnPropertyChanged(nameof(PrimaryActionText));
        OnPropertyChanged(nameof(SizeAndDrive));
        OnPropertyChanged(nameof(InstallDrive));
        OnPropertyChanged(nameof(InstallSize));
        OnPropertyChanged(nameof(InstallFullPath));
        OnPropertyChanged(nameof(InstallationName));
        OnPropertyChanged(nameof(InstallationRootPath));
        RefreshLoginPreference(_globalLoginPreference);
    }

    public void OnOwnerChanged()
    {
        RefreshLoginPreference(_globalLoginPreference);
        OnPropertyChanged(nameof(PrimaryActionText));
        OnPropertyChanged(nameof(HasOwner));
        OnPropertyChanged(nameof(OwnerAccountName));
    }
}
