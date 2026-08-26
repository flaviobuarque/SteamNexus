using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;
using SteamSwitcher.Core.Models;
using SteamSwitcher.Services.Themes;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace SteamSwitcher.Views.Dialogs;

public partial class ThemeEditorWindow : Window
{
    private readonly AppTheme _originalBaseTheme;
    private readonly CustomThemeSettings? _originalCustomTheme;
    private readonly DispatcherTimer _previewTimer;
    private bool _accepted;
    private bool _ready;

    public ThemeEditorViewModel ViewModel { get; }
    public CustomThemeSettings? ResultTheme { get; private set; }

    public ThemeEditorWindow(AppTheme baseTheme, CustomThemeSettings? customTheme)
    {
        _originalBaseTheme = baseTheme;
        _originalCustomTheme = customTheme?.Clone();
        var hasActiveCustomTheme = customTheme is { IsEnabled: true };
        var activePreset = hasActiveCustomTheme && ThemeEditorViewModel.IsBuiltInPreset(customTheme!.Name)
            ? customTheme.Name
            : null;
        var hasEditableCustomTheme = hasActiveCustomTheme && activePreset is null;
        ViewModel = new ThemeEditorViewModel(
            hasActiveCustomTheme ? customTheme!.Clone() : (baseTheme == AppTheme.Light
                ? CustomThemeSettings.CreateLight()
                : CustomThemeSettings.CreateDark()));
        ViewModel.SelectedPreset = activePreset
            ?? (baseTheme == AppTheme.Light ? "SteamNexus Light" : "SteamNexus Dark");
        ViewModel.IsPresetReadOnly = !hasEditableCustomTheme;
        if (!hasEditableCustomTheme) ViewModel.LoadSelectedPreset(asCopy: false);

        // Alguns templates do WPF UI resolvem as cores ao serem construídos.
        // Aplique a prévia antes de carregar o XAML para não preservar o accent anterior.
        var initialPreview = ViewModel.BuildTheme();
        CustomThemeManager.Validate(initialPreview);
        App.ApplyTheme(initialPreview.BaseTheme, initialPreview);

        InitializeComponent();
        DataContext = ViewModel;
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            PreviewTheme();
        };
        _ready = true;
        PreviewTheme();
        Closed += (_, _) =>
        {
            if (!_accepted) App.ApplyTheme(_originalBaseTheme, _originalCustomTheme);
        };
    }

    private void EditorValue_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void Preset_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        ViewModel.LoadSelectedPreset(asCopy: false);
        PreviewTheme();
    }

    private void PreviewTheme()
    {
        try
        {
            var theme = ViewModel.BuildTheme();
            CustomThemeManager.Validate(theme);
            App.ApplyTheme(theme.BaseTheme, theme);
            ViewModel.UpdateContrast(theme);
            ViewModel.ValidationMessage = string.Empty;
        }
        catch (Exception ex)
        {
            ViewModel.ValidationMessage = ex.Message;
        }
    }

    private void ChooseBackground_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolha uma imagem de fundo",
            Filter = "Imagens|*.png;*.jpg;*.jpeg;*.webp;*.bmp",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            ViewModel.BackgroundImagePath = CustomThemeManager.CopyBackgroundToLibrary(dialog.FileName);
            PreviewTheme();
        }
        catch (Exception ex) { ViewModel.ValidationMessage = ex.Message; }
    }

    private void RemoveBackground_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.BackgroundImagePath = null;
        PreviewTheme();
    }

    private async void ImportTheme_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Importar tema do SteamNexus",
            Filter = "Tema SteamNexus (*.steamnexus-theme)|*.steamnexus-theme|JSON (*.json)|*.json",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            ViewModel.Load(await CustomThemeManager.ImportAsync(dialog.FileName));
            ViewModel.IsPresetReadOnly = false;
            PreviewTheme();
        }
        catch (Exception ex) { ViewModel.ValidationMessage = ex.Message; }
    }

    private async void ExportTheme_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Exportar tema do SteamNexus",
            Filter = "Tema SteamNexus (*.steamnexus-theme)|*.steamnexus-theme",
            FileName = SanitizeFileName(ViewModel.ThemeName) + ".steamnexus-theme",
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var theme = ViewModel.BuildTheme();
            CustomThemeManager.Validate(theme);
            await CustomThemeManager.ExportAsync(theme, dialog.FileName);
            ViewModel.ValidationMessage = "Tema exportado com sucesso.";
        }
        catch (Exception ex) { ViewModel.ValidationMessage = ex.Message; }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Load(ViewModel.BaseTheme == AppTheme.Light
            ? CustomThemeSettings.CreateLight()
            : CustomThemeSettings.CreateDark());
        ViewModel.IsPresetReadOnly = false;
        PreviewTheme();
    }

    private void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.LoadSelectedPreset(asCopy: true);
        PreviewTheme();
    }

    private void CreateTheme_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CreateTheme();
        PreviewTheme();
    }

    private void BackToPresets_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.LoadSelectedPreset(asCopy: false);
        PreviewTheme();
    }

    private void ColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ThemeColorEntry color }) return;
        var dialog = new ThemeColorPickerDialog(color.Value) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        color.Value = dialog.SelectedColor;
        PreviewTheme();
    }

    private void FixContrast_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.FixContrast();
        PreviewTheme();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var theme = ViewModel.BuildTheme();
            CustomThemeManager.Validate(theme);
            ResultTheme = theme;
            _accepted = true;
            DialogResult = true;
        }
        catch (Exception ex) { ViewModel.ValidationMessage = ex.Message; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '-');
        return string.IsNullOrWhiteSpace(value) ? "tema-steamnexus" : value.Trim();
    }
}

public partial class ThemeEditorViewModel : ObservableObject
{
    public ObservableCollection<ThemeColorEntry> Colors { get; } = [];
    public IReadOnlyList<ThemeStretchOption> StretchOptions { get; } =
    [
        new("Preencher sem distorcer", "UniformToFill"),
        new("Exibir imagem inteira", "Uniform"),
        new("Esticar para preencher", "Fill"),
        new("Usar tamanho original", "None"),
    ];
    public IReadOnlyList<string> Presets { get; } = CustomThemeManager.BuiltInPresetNames;

    public static bool IsBuiltInPreset(string? name) =>
        name is not null && CustomThemeManager.BuiltInPresetNames.Contains(name);

    [ObservableProperty] private string _themeName = "Meu tema";
    [ObservableProperty] private string _selectedPreset = "SteamNexus Dark";
    [ObservableProperty] private AppTheme _baseTheme = AppTheme.Dark;
    [ObservableProperty] private string? _backgroundImagePath;
    [ObservableProperty] private double _backgroundOpacity = 0.25;
    [ObservableProperty] private double _backgroundBlurRadius;
    [ObservableProperty] private string _backgroundOverlay = "#6607111F";
    [ObservableProperty] private string _backgroundStretch = "UniformToFill";
    [ObservableProperty] private double _cardCornerRadius = 14;
    [ObservableProperty] private double _buttonCornerRadius = 8;
    [ObservableProperty] private double _borderOpacity = 1;
    [ObservableProperty] private bool _shadowsEnabled = true;
    [ObservableProperty] private string _contrastStatus = "Contraste adequado";
    [ObservableProperty] private string _contrastDetail = string.Empty;
    [ObservableProperty] private string _validationMessage = string.Empty;
    [ObservableProperty] private bool _isPresetReadOnly;

    public bool IsCustomTheme => !IsPresetReadOnly;
    public string BaseThemeLabel => BaseTheme == AppTheme.Light ? "Base clara" : "Base escura";
    public string PresetDescription => SelectedPreset switch
    {
        "SteamNexus Light" => "Visual claro, limpo e com contraste equilibrado para ambientes iluminados.",
        "AMOLED" => "Preto profundo, superfícies discretas e menor emissão de luz em telas OLED.",
        "Roxo neon" => "Composição escura com destaques vibrantes em roxo e magenta.",
        _ => "Visual original do SteamNexus, com tons escuros e destaques em azul.",
    };

    public string BackgroundImageDisplay => string.IsNullOrWhiteSpace(BackgroundImagePath)
        ? "Nenhuma imagem selecionada"
        : BackgroundImagePath;

    public ThemeEditorViewModel(CustomThemeSettings theme) => Load(theme);

    partial void OnBackgroundImagePathChanged(string? value) => OnPropertyChanged(nameof(BackgroundImageDisplay));
    partial void OnIsPresetReadOnlyChanged(bool value) => OnPropertyChanged(nameof(IsCustomTheme));
    partial void OnBaseThemeChanged(AppTheme value) => OnPropertyChanged(nameof(BaseThemeLabel));
    partial void OnSelectedPresetChanged(string value) => OnPropertyChanged(nameof(PresetDescription));

    public void Load(CustomThemeSettings theme)
    {
        ThemeName = theme.Name;
        BaseTheme = theme.BaseTheme;
        BackgroundImagePath = theme.BackgroundImagePath;
        BackgroundOpacity = theme.BackgroundImageOpacity;
        BackgroundBlurRadius = theme.BackgroundBlurRadius;
        BackgroundOverlay = theme.BackgroundOverlay;
        BackgroundStretch = theme.BackgroundStretch;
        CardCornerRadius = theme.CardCornerRadius;
        ButtonCornerRadius = theme.ButtonCornerRadius;
        BorderOpacity = theme.BorderOpacity;
        ShadowsEnabled = theme.ShadowsEnabled;

        Colors.Clear();
        Add("Fundo principal", "Background", theme.Background);
        Add("Barra lateral e rodapé", "Chrome", theme.Chrome);
        Add("Painéis e cartões", "Surface", theme.Surface);
        Add("Superfície secundária", "SurfaceAlt", theme.SurfaceAlt);
        Add("Hover", "SurfaceHover", theme.SurfaceHover);
        Add("Bordas", "Border", theme.Border);
        Add("Foco", "Focus", theme.Focus);
        Add("Texto principal", "TextPrimary", theme.TextPrimary);
        Add("Texto secundário", "TextSecondary", theme.TextSecondary);
        Add("Texto discreto", "TextMuted", theme.TextMuted);
        Add("Destaque", "Accent", theme.Accent);
        Add("Destaque alternativo", "AccentAlt", theme.AccentAlt);
        Add("Superfície de destaque", "AccentSurface", theme.AccentSurface);
        Add("Sucesso", "Success", theme.Success);
        Add("Aviso", "Warning", theme.Warning);
        Add("Erro", "Danger", theme.Danger);
        UpdateContrast(theme);
    }

    public CustomThemeSettings BuildTheme()
    {
        string Get(string key) => Colors.First(item => item.Key == key).Value;
        return new CustomThemeSettings
        {
            Name = ThemeName,
            BaseTheme = BaseTheme,
            IsEnabled = true,
            Background = Get("Background"), Chrome = Get("Chrome"),
            Surface = Get("Surface"), SurfaceAlt = Get("SurfaceAlt"),
            SurfaceHover = Get("SurfaceHover"), Border = Get("Border"), Focus = Get("Focus"),
            TextPrimary = Get("TextPrimary"), TextSecondary = Get("TextSecondary"),
            TextMuted = Get("TextMuted"), Accent = Get("Accent"), AccentAlt = Get("AccentAlt"),
            AccentSurface = Get("AccentSurface"), Success = Get("Success"),
            Warning = Get("Warning"), Danger = Get("Danger"),
            BackgroundImagePath = BackgroundImagePath,
            BackgroundImageOpacity = BackgroundOpacity,
            BackgroundBlurRadius = BackgroundBlurRadius,
            BackgroundOverlay = BackgroundOverlay,
            BackgroundStretch = BackgroundStretch,
            CardCornerRadius = CardCornerRadius,
            ButtonCornerRadius = ButtonCornerRadius,
            BorderOpacity = BorderOpacity,
            ShadowsEnabled = ShadowsEnabled,
        };
    }

    public void UpdateContrast(CustomThemeSettings theme)
    {
        var primary = CustomThemeManager.ContrastRatio(theme.TextPrimary, theme.Surface);
        var secondary = CustomThemeManager.ContrastRatio(theme.TextSecondary, theme.Surface);
        ContrastStatus = primary >= 4.5 && secondary >= 3 ? "Contraste adequado" : "Revisar contraste";
        ContrastDetail = $"Texto principal: {primary:F1}:1 • Texto secundário: {secondary:F1}:1. "
            + (primary >= 4.5 ? "O texto principal atende ao contraste recomendado." :
                "Aumente a diferença entre o texto principal e os cartões.");
    }

    public void LoadSelectedPreset(bool asCopy)
    {
        var theme = CustomThemeManager.CreateBuiltInPreset(SelectedPreset);
        theme.Name = asCopy ? $"Cópia de {SelectedPreset}" : SelectedPreset;
        Load(theme);
        IsPresetReadOnly = !asCopy;
    }

    public void CreateTheme()
    {
        var theme = BaseTheme == AppTheme.Light
            ? CustomThemeSettings.CreateLight()
            : CustomThemeSettings.CreateDark();
        theme.Name = "Novo tema";
        Load(theme);
        IsPresetReadOnly = false;
    }

    public void FixContrast()
    {
        var surface = Colors.First(item => item.Key == "Surface").Value;
        var whiteRatio = CustomThemeManager.ContrastRatio("#FFFFFF", surface);
        var blackRatio = CustomThemeManager.ContrastRatio("#0B1220", surface);
        var useLightText = whiteRatio >= blackRatio;
        Colors.First(item => item.Key == "TextPrimary").Value = useLightText ? "#FFFFFF" : "#0B1220";
        Colors.First(item => item.Key == "TextSecondary").Value = useLightText ? "#CBD5E1" : "#334155";
        Colors.First(item => item.Key == "TextMuted").Value = useLightText ? "#94A3B8" : "#64748B";
    }

    private void Add(string label, string key, string value) => Colors.Add(new ThemeColorEntry(label, key, value));
}

public partial class ThemeColorEntry(string label, string key, string value) : ObservableObject
{
    public string Label { get; } = label;
    public string Key { get; } = key;
    [ObservableProperty] private string _value = value;
    public Brush PreviewBrush
    {
        get
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(Value)!); }
            catch { return Brushes.Transparent; }
        }
    }
    partial void OnValueChanged(string value) => OnPropertyChanged(nameof(PreviewBrush));
}

public sealed record ThemeStretchOption(string Label, string Value);
