using SteamSwitcher.Core.Helpers;
using SteamSwitcher.Core.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SteamSwitcher.Views.Pages;

public partial class ActivityPage : Page
{
    private readonly Helpers.PageScrollMemory _scrollMemory = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private IReadOnlyList<ActivityEntry> _displayedEntries = [];
    private ActivityEntry[] _filtered = [];
    private readonly ObservableCollection<ActivityEntry> _visible = [];
    private bool _ready, _resetting, _loading;
    private ScrollViewer? ListScroll => Entries.Template?.FindName("PART_ScrollViewer", Entries) as ScrollViewer;
#if DEBUG
    private bool _previewEnabled = true;
#else
    private bool _previewEnabled = false;
#endif
    private readonly IReadOnlyList<ActivityEntry> _previewEntries = CreatePreviewEntries();
    private static IReadOnlyList<ActivityEntry> CreatePreviewEntries()
    {
        var kinds = Enum.GetValues<ActivityKind>();
        var now = DateTimeOffset.Now;
        return Enumerable.Range(0, 200).Select(i => new ActivityEntry(
            now.AddMinutes(-i * 47), kinds[i % kinds.Length],
            i % 7 == 0 ? null : $"Conta de demonstração {i % 5 + 1} (@demo_origem_{i % 5 + 1})",
            $"Perfil de teste — biblioteca compartilhada {i % 8 + 1} (@demo_destino_{i % 8 + 1})",
            i % 3 == 0 ? "Jogo de demonstração com título longo — Edição definitiva e conteúdo adicional" : $"Jogo de teste {i + 1}",
            ((i / kinds.Length) % 3) switch { 0 => "Lembrar senha (RememberPassword)", 1 => "Desativar seletor obrigatório de contas", _ => "Corrigir autologin no registro" },
            i % 2 == 0 ? "C: · Principal" : "D: · Secundária"))
            .ToArray();
    }

    public ActivityPage()
    {
        InitializeComponent();
        Entries.ItemsSource = _visible;
        _ready = true;
        PeriodPicker.SetBounds(null);
        PeriodPicker.PeriodChanged += (_, _) => ApplyFilters();
#if DEBUG
        PreviewToggle.Visibility = Visibility.Visible;
#endif
        _refreshTimer.Tick += (_, _) => Refresh();
        Loaded += async (_, _) =>
        {
            Refresh(true);
            await _scrollMemory.RestoreAsync(this);
            if (IsLoaded) _refreshTimer.Start();
        };
        Unloaded += (_, _) => { _refreshTimer.Stop(); _scrollMemory.Capture(this); };
    }

    private void Refresh(bool accept = false)
    {
        var entries = _previewEnabled ? _previewEntries : ActivityJournal.Shared.Snapshot();
        if (!_displayedEntries.SequenceEqual(entries))
        {
            if (accept || _displayedEntries.Count == 0)
            {
                _displayedEntries = entries;
                PeriodPicker.SetBounds(entries.Count == 0 ? null : entries.Min(entry => entry.Timestamp.LocalDateTime));
                UpdateKpis();
                ApplyFilters();
                NewActivity.Visibility = Visibility.Collapsed;
            }
            else
            {
                NewActivity.Visibility = Visibility.Visible;
            }
        }
        else
        {
            UpdateKpis();
        }

        StorageNotice.Text = _previewEnabled
            ? "DEMONSTRAÇÃO · 200 registros fictícios. Seu histórico real está preservado."
            : ActivityJournal.Shared.StorageError
            ?? "Últimas 200 operações salvas neste computador. Sem senhas ou chaves.";
        ClearButton.IsEnabled = !_previewEnabled && entries.Count > 0;
        PreviewToggle.Content = _previewEnabled ? "Sair da prévia" : "Prévia com 200 registros";
    }

    private void UpdateKpis()
    {
        var total = _displayedEntries.Count;
        var switches = _displayedEntries.Count(e => e.Kind is ActivityKind.AccountSwitched or ActivityKind.AccountSwitchFailed);
        var games = _displayedEntries.Count(e => e.Kind is ActivityKind.GameStarted or ActivityKind.GameLaunchFailed);
        var attention = _displayedEntries.Count(e => e.NeedsAttention);

        KpiTotalText.Text = total.ToString();
        KpiSwitchesText.Text = switches.ToString();
        KpiGamesText.Text = games.ToString();
        KpiAttentionText.Text = attention.ToString();

        if (attention > 0)
        {
            KpiAttentionText.Foreground = (Brush)FindResource("AppDangerBrush");
            KpiAttentionIcon.Foreground = (Brush)FindResource("AppDangerBrush");
            KpiAttentionIconBorder.Background = (Brush)FindResource("AppDangerSurfaceBrush");
            KpiAttentionCard.BorderBrush = (Brush)FindResource("AppDangerBrush");
        }
        else
        {
            KpiAttentionText.Foreground = (Brush)FindResource("AppTextPrimaryBrush");
            KpiAttentionIcon.Foreground = (Brush)FindResource("AppTextMutedBrush");
            KpiAttentionIconBorder.Background = (Brush)FindResource("AppSurfaceAltBrush");
            KpiAttentionCard.BorderBrush = (Brush)FindResource("AppBorderBrush");
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) { Refresh(true); ApplyFilters(); }

    private void FilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_ready && !_resetting) ApplyFilters();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_ready && !_resetting) ApplyFilters();
    }

    private void ApplyFilters()
    {
        var hasSearch = !string.IsNullOrWhiteSpace(SearchBox.Text);
        var hasPeriod = PeriodPicker.Start.HasValue;
        var hasType = TypeFilter.SelectedIndex > 0;
        ClearFiltersButton.Visibility = (hasPeriod || hasType || hasSearch) ? Visibility.Visible : Visibility.Collapsed;

        _filtered = ActivityFilter.Apply(_displayedEntries, PeriodPicker.Start, PeriodPicker.End, TypeFilter.SelectedIndex, SearchBox.Text);
        _visible.Clear();
        AddBatch();
        ListScroll?.ScrollToTop();

        var isEmpty = _filtered.Length == 0;
        EmptyStatePanel.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        Entries.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
        if (isEmpty)
        {
            Empty.Text = _displayedEntries.Count == 0
                ? "Nenhuma operação registrada ainda."
                : "Nenhum registro encontrado.";
        }

        ExportButton.IsEnabled = _filtered.Length > 0;
    }

    private void AddBatch()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            foreach (var entry in _filtered.Skip(_visible.Count).Take(30)) _visible.Add(entry);
            Summary.Text = $"Mostrando {_visible.Count} de {_filtered.Length} registros · Mais recentes primeiro";
            EndNotice.Visibility = _filtered.Length > 0 && _visible.Count == _filtered.Length ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _loading = false; }
    }
    private void Top_Click(object sender, RoutedEventArgs e) => ListScroll?.ScrollToTop();

    private void Entries_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!_ready || e.OriginalSource is not ScrollViewer scroll) return;
        TopButton.Visibility = scroll.VerticalOffset > 200 ? Visibility.Visible : Visibility.Collapsed;
        if (e.VerticalChange > 0 && e.ExtentHeightChange == 0 && scroll.ScrollableHeight - scroll.VerticalOffset < 120) AddBatch();
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        _resetting = true;
        PeriodPicker.Clear();
        TypeFilter.SelectedIndex = 0;
        SearchBox.Text = string.Empty;
        _resetting = false;
        ApplyFilters();
    }

    private void KpiTotal_Click(object sender, RoutedEventArgs e)
    {
        _resetting = true;
        PeriodPicker.Clear();
        TypeFilter.SelectedIndex = 0;
        SearchBox.Text = string.Empty;
        _resetting = false;
        ApplyFilters();
    }

    private void KpiSwitches_Click(object sender, RoutedEventArgs e)
    {
        TypeFilter.SelectedIndex = 1;
    }

    private void KpiGames_Click(object sender, RoutedEventArgs e)
    {
        TypeFilter.SelectedIndex = 2;
    }

    private void KpiAttention_Click(object sender, RoutedEventArgs e)
    {
        TypeFilter.SelectedIndex = 6;
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_filtered.Length == 0) return;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Histórico de Atividades - SteamSwitcher ({DateTime.Now:dd/MM/yyyy HH:mm:ss})");
        sb.AppendLine($"# Total de registros: {_filtered.Length}");
        sb.AppendLine(new string('-', 60));
        foreach (var entry in _filtered)
        {
            sb.AppendLine($"[{entry.TimeText}] [{entry.CategoryName}] {entry.Title} ({(entry.NeedsAttention ? "FALHA" : "OK")})");
            if (!string.IsNullOrWhiteSpace(entry.Detail))
            {
                sb.AppendLine($"  {entry.Detail.Replace("\n", "\n  ")}");
            }
            sb.AppendLine();
        }
        try
        {
            Clipboard.SetText(sb.ToString());
            ExportStatusText.Text = "Copiado para a área de transferência!";
            ExportStatusText.Visibility = Visibility.Visible;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) => { ExportStatusText.Visibility = Visibility.Collapsed; timer.Stop(); };
            timer.Start();
        }
        catch { }
    }

    private void CopyButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        CopyEntry_Click(sender, e);
    }

    private void CopyEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ActivityEntry entry })
        {
            try
            {
                var text = $"[{entry.TimeText}] [{entry.CategoryName}] {entry.Title}\n{entry.Detail}";
                Clipboard.SetText(text);
                ExportStatusText.Text = "Registro copiado!";
                ExportStatusText.Visibility = Visibility.Visible;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (_, _) => { ExportStatusText.Visibility = Visibility.Collapsed; timer.Stop(); };
                timer.Start();
            }
            catch { }
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_previewEnabled) return;
        var confirm = new Dialogs.ConfirmDialog("Limpar histórico?", "Os registros salvos serão removidos deste computador. Esta ação não altera suas contas ou jogos.", "Limpar histórico", kind: Dialogs.ConfirmDialog.DialogKind.Danger)
        { Owner = Window.GetWindow(this) };
        confirm.ShowDialog();
        if (!confirm.Confirmed) return;
        ActivityJournal.Shared.Clear();
        Refresh(true);
    }

    private void Monitoring_Click(object sender, RoutedEventArgs e)
        => new Dialogs.MonitoringDialog { Owner = Window.GetWindow(this) }.ShowDialog();

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
#if DEBUG
        _previewEnabled = !_previewEnabled;
        _scrollMemory.Reset(this);
        Refresh(true);
        ApplyFilters();
#endif
    }
}
