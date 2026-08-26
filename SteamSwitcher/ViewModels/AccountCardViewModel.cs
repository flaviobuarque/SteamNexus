using CommunityToolkit.Mvvm.ComponentModel;
using SteamSwitcher.Core.Models;
using System.Windows.Media;

namespace SteamSwitcher.ViewModels;

public partial class AccountCardViewModel(SteamAccount account) : ObservableObject
{
    private int _avatarLoadStarted;

    public SteamAccount Account { get; } = account;

    [ObservableProperty] private string _avatarPath = string.Empty;
    [ObservableProperty] private ImageSource? _avatarImage;
    [ObservableProperty] private bool _isActive = account.IsActive;
    [ObservableProperty] private bool _isSwitching;
    [ObservableProperty] private bool _isPendingRemoval;
    [ObservableProperty] private bool _isFavorite = account.IsFavorite;
    [ObservableProperty] private bool _showInstallationBadge;
    [ObservableProperty] private double _removalProgress = 100;
    [ObservableProperty] private string _removalCountdownText = string.Empty;

    public string DisplayName => Account.DisplayName;
    public string AccountName => Account.AccountName;
    public string SteamId64 => Account.SteamId64;
    public string UniqueKey => Account.UniqueKey;
    public string InstallationId => Account.InstallationId;
    public string InstallationName => Account.InstallationName;
    public string InstallationRootPath => Account.InstallationRootPath;
    public long Timestamp => Account.Timestamp;
    public string LastLoginFormatted => FormatLastLogin(Account.Timestamp);
    public bool HasAvatar => AvatarImage is not null || !string.IsNullOrEmpty(AvatarPath);

    public bool TryBeginAvatarLoad()
        => Interlocked.CompareExchange(ref _avatarLoadStarted, 1, 0) == 0;

    public void PrepareAvatarReloadIfMissing()
    {
        if (!HasAvatar)
            Interlocked.Exchange(ref _avatarLoadStarted, 0);
    }

    public void ApplySnapshot(SteamAccount updated)
    {
        var avatarOverrideChanged = !string.Equals(
            Account.CustomAvatarPath,
            updated.CustomAvatarPath,
            StringComparison.OrdinalIgnoreCase);

        Account.AccountName = updated.AccountName;
        Account.InstallationId = updated.InstallationId;
        Account.InstallationName = updated.InstallationName;
        Account.InstallationRootPath = updated.InstallationRootPath;
        Account.PersonaName = updated.PersonaName;
        Account.RememberPassword = updated.RememberPassword;
        Account.MostRecent = updated.MostRecent;
        Account.AutoLogin = updated.AutoLogin;
        Account.Timestamp = updated.Timestamp;
        Account.WantsOfflineMode = updated.WantsOfflineMode;
        Account.AvatarUrl = updated.AvatarUrl;
        Account.CustomDisplayName = updated.CustomDisplayName;
        Account.CustomAvatarPath = updated.CustomAvatarPath;
        Account.LoginStateOverride = updated.LoginStateOverride;
        Account.IsFavorite = updated.IsFavorite;
        Account.IsActive = updated.IsActive;
        IsActive = updated.IsActive;
        IsFavorite = updated.IsFavorite;

        if (avatarOverrideChanged)
        {
            AvatarPath = string.Empty;
            AvatarImage = null;
            Interlocked.Exchange(ref _avatarLoadStarted, 0);
        }

        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(AccountName));
        OnPropertyChanged(nameof(UniqueKey));
        OnPropertyChanged(nameof(InstallationId));
        OnPropertyChanged(nameof(InstallationName));
        OnPropertyChanged(nameof(InstallationRootPath));
        OnPropertyChanged(nameof(Timestamp));
        OnPropertyChanged(nameof(LastLoginFormatted));
    }

    private static string FormatLastLogin(long timestamp)
    {
        if (timestamp == 0) return "Nunca";
        var dt = DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime;
        var diff = DateTime.Now - dt;
        return diff.TotalDays switch
        {
            < 1 => "Hoje",
            < 2 => "Ontem",
            < 7 => $"{(int)diff.TotalDays}d atrás",
            < 30 => $"{(int)(diff.TotalDays / 7)}sem atrás",
            < 365 => $"{(int)(diff.TotalDays / 30)}meses atrás",
            _ => $"{(int)(diff.TotalDays / 365)}a atrás"
        };
    }

    partial void OnAvatarPathChanged(string value) => OnPropertyChanged(nameof(HasAvatar));
    partial void OnAvatarImageChanged(ImageSource? value) => OnPropertyChanged(nameof(HasAvatar));

    public void RefreshDisplayName()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(AccountName));
    }

    partial void OnIsFavoriteChanged(bool value) => Account.IsFavorite = value;
}
