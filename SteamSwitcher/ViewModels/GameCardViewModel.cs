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
    public bool HasCustomLoginPreference => Game.LoginStateOverride.HasValue;
    public string CustomLoginPreferenceText => Game.LoginStateOverride switch
    {
        LoginState.Offline => "● Offline",
        LoginState.Online => "● Online",
        _ => string.Empty
    };
    public string CustomLoginBadgeText => Game.LoginStateOverride switch
    {
        LoginState.Offline => " · Offline",
        LoginState.Online => " · Online",
        _ => string.Empty
    };

    public string OwnerBadgeTooltip
    {
        get
        {
            var owner = HasOwner ? $"Conta: {OwnerAccountName}" : "Sem conta associada";
            var mode = Game.LoginStateOverride.HasValue
                ? $"Modo: {Helpers.LoginPreferencePresentation.StateName(Game.LoginStateOverride.Value)}"
                : "Modo: Padrão da conta";
            return $"{owner}\n{mode}\n(Clique com botão direito no card para opções)";
        }
    }

    public string StorageText
    {
        get
        {
            var size = InstallSize;
            var drive = InstallDrive;
            if (!string.IsNullOrEmpty(size) && !string.IsNullOrEmpty(drive))
                return $"{size} · {drive}";
            if (!string.IsNullOrEmpty(size))
                return size;
            if (!string.IsNullOrEmpty(drive))
                return $"Disco {drive}";
            return "Local desconhecido";
        }
    }

    public bool IsCustomOffline => Game.LoginStateOverride == LoginState.Offline;
    public bool IsCustomOnline => Game.LoginStateOverride == LoginState.Online;
    public string CustomLoginBadgeLabel => Game.LoginStateOverride switch
    {
        LoginState.Offline => "OFFLINE",
        LoginState.Online => "ONLINE",
        _ => string.Empty
    };

    public string OwnerAccountTooltip => HasOwner
        ? $"Conta proprietária: {OwnerAccountName}"
        : "Nenhuma conta proprietária associada";

    public string StorageDisplay
    {
        get
        {
            var drive = InstallDrive?.TrimEnd(':', '\\', '/');
            var size = InstallSize;
            if (!string.IsNullOrEmpty(drive) && !string.IsNullOrEmpty(size))
                return $"{drive}: {size}";
            if (!string.IsNullOrEmpty(size))
                return size;
            if (!string.IsNullOrEmpty(drive))
                return $"Disco {drive}:";
            return "-";
        }
    }

    public bool HasNeverPlayed => Game.PlaytimeMinutes <= 0;

    public string PlaytimeShortDisplay => Game.PlaytimeMinutes > 0
        ? Game.PlaytimeFormatted
        : "Nunca";

    public string PlaytimeTooltip => Game.PlaytimeMinutes > 0
        ? $"Tempo de jogo: {Game.PlaytimeFormatted} jogadas"
        : "Tempo de jogo: Nunca jogado";

    public string PlaytimeDisplay => Game.PlaytimeMinutes > 0
        ? $"{Game.PlaytimeFormatted} jogadas"
        : "Nunca jogado";

    public string SubtitleText
    {
        get
        {
            var size = InstallSize;
            var hasPlaytime = Game.PlaytimeMinutes > 0;
            var playtime = hasPlaytime ? Game.PlaytimeFormatted : string.Empty;

            if (!string.IsNullOrEmpty(size) && !string.IsNullOrEmpty(playtime))
                return $"{size} • {playtime}";
            if (!string.IsNullOrEmpty(size))
                return size;
            if (!string.IsNullOrEmpty(playtime))
                return playtime;
            return !string.IsNullOrEmpty(InstallDrive) ? $"Disco {InstallDrive}" : string.Empty;
        }
    }

    public void RefreshLoginPreference(LoginState? globalPreference)
    {
        _globalLoginPreference = globalPreference;
        OnPropertyChanged(nameof(LoginPreferenceText));
        OnPropertyChanged(nameof(LoginPreferenceTooltip));
        OnPropertyChanged(nameof(LoginPreferenceValue));
        OnPropertyChanged(nameof(LoginPreferenceInheritedText));
        OnPropertyChanged(nameof(HasCustomLoginPreference));
        OnPropertyChanged(nameof(CustomLoginPreferenceText));
        OnPropertyChanged(nameof(CustomLoginBadgeText));
        OnPropertyChanged(nameof(CustomLoginBadgeLabel));
        OnPropertyChanged(nameof(IsCustomOffline));
        OnPropertyChanged(nameof(IsCustomOnline));
        OnPropertyChanged(nameof(OwnerBadgeTooltip));
        OnPropertyChanged(nameof(OwnerAccountTooltip));
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
        OnPropertyChanged(nameof(StorageText));
        OnPropertyChanged(nameof(StorageDisplay));
        OnPropertyChanged(nameof(PlaytimeDisplay));
        OnPropertyChanged(nameof(PlaytimeShortDisplay));
        OnPropertyChanged(nameof(PlaytimeTooltip));
        OnPropertyChanged(nameof(HasNeverPlayed));
        OnPropertyChanged(nameof(SubtitleText));
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
