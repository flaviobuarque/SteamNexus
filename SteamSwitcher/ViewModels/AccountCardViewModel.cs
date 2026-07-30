using CommunityToolkit.Mvvm.ComponentModel;
using SteamSwitcher.Core.Models;
using System.Windows.Media;

namespace SteamSwitcher.ViewModels;

public partial class AccountCardViewModel(SteamAccount account) : ObservableObject
{
    public SteamAccount Account { get; } = account;

    [ObservableProperty] private string _avatarPath = string.Empty;
    [ObservableProperty] private ImageSource? _avatarImage;
    [ObservableProperty] private bool _isActive = account.IsActive;
    [ObservableProperty] private bool _isSwitching;
    [ObservableProperty] private bool _isPendingRemoval;
    [ObservableProperty] private bool _isVacBanned;
    [ObservableProperty] private bool _isGameBanned;
    [ObservableProperty] private bool _isLimited;
    [ObservableProperty] private bool _hasValidSession = true;

    public string DisplayName => Account.DisplayName;
    public string AccountName => Account.AccountName;
    public string LastLoginFormatted => FormatLastLogin(Account.Timestamp);
    public bool HasAvatar => AvatarImage is not null || !string.IsNullOrEmpty(AvatarPath);

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
}