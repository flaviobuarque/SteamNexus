using SteamSwitcher.Core.Helpers;
using SteamSwitcher.Core.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Collections.ObjectModel;

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
        ApplyFilters();
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
                ApplyFilters();
                NewActivity.Visibility = Visibility.Collapsed;
            }
            else NewActivity.Visibility = Visibility.Visible;
        }
        StorageNotice.Text = _previewEnabled
            ? "DEMONSTRAÇÃO · 200 registros fictícios. Seu histórico real está preservado."
            : ActivityJournal.Shared.StorageError
            ?? "Últimas 200 operações salvas neste computador. Sem senhas ou chaves.";
        ClearButton.IsEnabled = !_previewEnabled && entries.Count > 0;
        PreviewToggle.Content = _previewEnabled ? "Sair da prévia" : "Prévia com 200 registros";
    }
    private void Refresh_Click(object sender, RoutedEventArgs e) { Refresh(true); ApplyFilters(); }
    private void FilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_ready && !_resetting) ApplyFilters();
    }
    private void ApplyFilters()
    {
        ClearFiltersButton.Visibility = PeriodPicker.Start.HasValue || TypeFilter.SelectedIndex > 0 ? Visibility.Visible : Visibility.Collapsed;
        _filtered = ActivityFilter.Apply(_displayedEntries, PeriodPicker.Start, PeriodPicker.End, TypeFilter.SelectedIndex);
        _visible.Clear();
        AddBatch();
        ListScroll?.ScrollToTop();
        Empty.Text = _filtered.Length == 0 ? "Nenhum registro encontrado. Ajuste as datas ou limpe os filtros." : "";
        Empty.Visibility = _filtered.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void AddBatch()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            foreach (var entry in _filtered.Skip(_visible.Count).Take(30)) _visible.Add(entry);
            Summary.Text = $"Mostrando {_visible.Count} de {_filtered.Length} registros · Mais recentes primeiro";
            LoadMore.Visibility = _visible.Count < _filtered.Length ? Visibility.Visible : Visibility.Collapsed;
            EndNotice.Visibility = _filtered.Length > 0 && _visible.Count == _filtered.Length ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _loading = false; }
    }
    private void LoadMore_Click(object sender, RoutedEventArgs e) => AddBatch();
    private void Top_Click(object sender, RoutedEventArgs e) => ListScroll?.ScrollToTop();
    private void Entries_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!_ready || e.OriginalSource is not ScrollViewer scroll) return;
        TopButton.Visibility = scroll.VerticalOffset > 200 ? Visibility.Visible : Visibility.Collapsed;
        // Only user movement, never extent changes caused by appending a batch.
        if (e.VerticalChange > 0 && e.ExtentHeightChange == 0 && scroll.ScrollableHeight - scroll.VerticalOffset < 120) AddBatch();
    }
    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        _resetting = true;
        PeriodPicker.Clear();
        TypeFilter.SelectedIndex = 0;
        _resetting = false;
        ApplyFilters();
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
