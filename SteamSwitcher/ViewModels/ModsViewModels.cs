using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamSwitcher.Core;
using SteamSwitcher.Core.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SteamSwitcher.ViewModels;

public partial class ModGroup(string title, ModType type, IEnumerable<DetectedMod> items)
    : ObservableObject
{
    public string Title { get; } = title;
    public ModType Type { get; } = type;
    public IReadOnlyList<DetectedMod> Items { get; } = items.ToList();
    public int Count => Items.Count;
    public bool HasSuspicious => Items.Any(m => m.IsSuspicious);

    [ObservableProperty] private bool _isExpanded = false;

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;
}

public partial class ModsViewModel(
    IModMonitorService modMonitor,
    ISteamLocatorService locator,
    ISnackbarService snackbarService,
    MainViewModel mainViewModel) : ObservableObject
{
    private static readonly string _cachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamSwitcher", "mods_cache.json");

    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    [ObservableProperty] private ObservableCollection<DetectedMod> _mods = [];
    [ObservableProperty] private ObservableCollection<ModGroup> _groups = [];
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasSuspicious;

    public bool IsEmpty => !IsLoading && Mods.Count == 0;

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));
    partial void OnModsChanged(ObservableCollection<DetectedMod> value)
    {
        OnPropertyChanged(nameof(IsEmpty));
        RebuildGroups();
    }

    private void RebuildGroups()
    {
        var grouped = Mods
            .GroupBy(m => m.Type)
            .OrderBy(g => g.Key.ToString())
            .Select(g => new ModGroup(
                g.Key switch
                {
                    ModType.Millennium => "Millennium",
                    ModType.Plugin => "Plugins",
                    ModType.Skin => "Skins",
                    _ => "Outros"
                },
                g.Key,
                g.OrderBy(m => m.Name)))
            .ToList();

        Groups = new ObservableCollection<ModGroup>(grouped);
        RefreshStatusBar();
    }

    public void RefreshStatusBar()
    {
        var suspicious = Mods.Count(m => m.IsSuspicious);
        var left = suspicious > 0
            ? $"{Mods.Count} mods · {suspicious} suspeito(s)"
            : $"{Mods.Count} mods detectados";
        mainViewModel.UpdateStatusBar(left);
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (!FeatureFlags.Mods) return;

        await LoadCacheAsync();

        var steamPath = locator.FindSteamInstallPath();
        if (!string.IsNullOrEmpty(steamPath))
            modMonitor.StartWatching(steamPath);
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct)
    {
        var steamPath = locator.FindSteamInstallPath();
        if (string.IsNullOrEmpty(steamPath)) return;

        IsLoading = true;
        try
        {
            await modMonitor.ScanAsync(steamPath, ct);
            Mods = new ObservableCollection<DetectedMod>(modMonitor.DetectedMods);
            HasSuspicious = Mods.Any(m => m.IsSuspicious);
            await SaveCacheAsync();
            RefreshStatusBar();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadCacheAsync()
    {
        if (!File.Exists(_cachePath)) return;
        try
        {
            var json = await File.ReadAllTextAsync(_cachePath);
            var cached = JsonSerializer.Deserialize<List<CachedMod>>(json, _json);
            if (cached is null || cached.Count == 0) return;

            var mods = cached.Select(c => new DetectedMod
            {
                Name = c.Name,
                Version = c.Version,
                Type = c.Type,
                Path = c.Path,
                IsSuspicious = c.IsSuspicious
            }).ToList();

            Mods = new ObservableCollection<DetectedMod>(mods);
            HasSuspicious = Mods.Any(m => m.IsSuspicious);
        }
        catch { }
        RefreshStatusBar();
    }

    private async Task SaveCacheAsync()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            var cached = Mods.Select(m => new CachedMod
            {
                Name = m.Name,
                Version = m.Version,
                Type = m.Type,
                Path = m.Path,
                IsSuspicious = m.IsSuspicious
            }).ToList();

            var json = JsonSerializer.Serialize(cached, _json);
            await File.WriteAllTextAsync(_cachePath, json);
        }
        catch { }
    }

    [RelayCommand]
    private void ReinstallCleanSteam()
    {
        snackbarService.Show(
            "Reinstalação limpa",
            "Feature disponível em breve. Backup das contas será feito automaticamente.",
            ControlAppearance.Caution,
            null,
            TimeSpan.FromSeconds(4));
    }

    // DTO para serialização (DetectedMod tem required que complica deserialização)
    private class CachedMod
    {
        public string Name { get; set; } = string.Empty;
        public string? Version { get; set; }
        public ModType Type { get; set; }
        public string Path { get; set; } = string.Empty;
        public bool IsSuspicious { get; set; }
    }
}