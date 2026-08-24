using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SteamSwitcher.Core.Models;
using SteamSwitcher.Core.Services;
using SteamSwitcher.Views.Dialogs;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SteamSwitcher.ViewModels;

public partial class EditAccountViewModel(
    IAccountOverrideService overrideService,
    ISnackbarService snackbarService) : ObservableObject
{
    private SteamAccount? _account;
    private string _steamAvatarPath = string.Empty;

    [ObservableProperty] private string _customDisplayName = string.Empty;
    [ObservableProperty] private string _customAvatarPath = string.Empty;
    [ObservableProperty] private string _avatarPreviewPath = string.Empty;
    [ObservableProperty] private int _selectedLoginState = -1;

    public static IReadOnlyList<LoginStateItem> LoginStateOptions { get; } =
[
    new() { Value = -1, Label = "Padrão global", Icon = "Settings",   Color = "#6593B0" },
    new() { Value =  1, Label = "Online",        Icon = "Circle",      Color = "#57E389" },
    new() { Value =  0, Label = "Offline",       Icon = "CircleOff",   Color = "#6593B0" },
    new() { Value =  7, Label = "Invisível",      Icon = "EyeOff",      Color = "#A9C8DE" },
    new() { Value =  3, Label = "Away",           Icon = "Clock",       Color = "#F2B84B" },
];

    [ObservableProperty]
    private LoginStateItem? _selectedLoginStateItem;

    public void Load(SteamAccount account, string steamAvatarPath)
    {
        _account = account;
        _steamAvatarPath = steamAvatarPath;

        CustomDisplayName = account.CustomDisplayName ?? string.Empty;
        CustomAvatarPath = account.CustomAvatarPath ?? string.Empty;

        AvatarPreviewPath = !string.IsNullOrWhiteSpace(CustomAvatarPath)
            ? CustomAvatarPath
            : _steamAvatarPath;

        var stateValue = account.LoginStateOverride.HasValue
            ? (int)account.LoginStateOverride.Value
            : -1;

        SelectedLoginStateItem = LoginStateOptions.FirstOrDefault(o => o.Value == stateValue)
            ?? LoginStateOptions[0];
    }

    partial void OnCustomAvatarPathChanged(string value)
    {
        AvatarPreviewPath = string.IsNullOrWhiteSpace(value)
            ? _steamAvatarPath
            : value;
    }

    [RelayCommand]
    private void BrowseAvatar()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecionar imagem de avatar",
            Filter = "Imagens|*.jpg;*.jpeg;*.png;*.bmp;*.webp",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
            CustomAvatarPath = dialog.FileName;
    }

    [RelayCommand]
    private void RemoveAvatar() => CustomAvatarPath = string.Empty;

    [RelayCommand]
    internal async Task SaveAsync()
    {
        if (_account is null) return;

        var existing = await overrideService.GetOverrideAsync(_account.SteamId64);

        var override_ = new AccountOverride
        {
            CustomDisplayName = string.IsNullOrWhiteSpace(CustomDisplayName)
                ? null : CustomDisplayName.Trim(),
            CustomAvatarPath = string.IsNullOrWhiteSpace(CustomAvatarPath)
                ? null : CustomAvatarPath,
            LoginStateOverride = SelectedLoginStateItem?.Value is null or -1
                ? null
                : (LoginState?)SelectedLoginStateItem.Value,
            IsFavorite = existing?.IsFavorite ?? _account.IsFavorite,
        };

        await overrideService.SaveOverrideAsync(_account.SteamId64, override_);

        // Aplica ao model em memória
        _account.CustomDisplayName = override_.CustomDisplayName;
        _account.CustomAvatarPath = override_.CustomAvatarPath;
        _account.LoginStateOverride = override_.LoginStateOverride;

        snackbarService.Show(
            "Conta atualizada",
            "As alterações foram salvas e refletem apenas neste app.",
            ControlAppearance.Success,
            null,
            TimeSpan.FromSeconds(3));
    }
}
