using SteamSwitcher.Core.Helpers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SteamSwitcher.Views.Dialogs;

public partial class QuickSearchDialog : Window
{
    private readonly IReadOnlyList<QuickSearchEntry> _entries;
    private readonly IReadOnlyList<string> _recentIds;
    public QuickSearchEntry? Selected { get; private set; }
    public QuickSearchDialog(IReadOnlyList<QuickSearchEntry> entries, IReadOnlyList<string> recentIds)
    {
        _entries = entries;
        _recentIds = recentIds;
        InitializeComponent();
        Helpers.DialogChrome.Apply(this);
        PreviewKeyDown += OnKey;
        Loaded += (_, _) => { Refresh(); SearchBox.Focus(); };
    }
    private void SearchChanged(object sender, TextChangedEventArgs e) { if (IsLoaded) Refresh(); }
    private void Refresh()
    {
        Results.ItemsSource = QuickSearch.Find(_entries, SearchBox.Text, _recentIds);
        Results.SelectedIndex = Results.Items.Count > 0 ? 0 : -1;
        EmptyState.Visibility = Results.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Hint.Text = string.IsNullOrWhiteSpace(SearchBox.Text) ? "Acesso rápido"
            : Results.Items.Count == 40 ? "40 resultados · refine a busca para encontrar mais"
            : Results.Items.Count == 1 ? "1 resultado" : $"{Results.Items.Count} resultados";
    }
    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OpenButton is null) return;
        OpenButton.IsEnabled = Results.SelectedItem is QuickSearchEntry;
        OpenButton.Content = (Results.SelectedItem as QuickSearchEntry)?.Category switch
        { "Jogo" => "Ver jogo", "Conta" => "Ver conta", null => "Selecione uma opção", _ => "Abrir página" };
    }
    private void OpenSelected(object sender, RoutedEventArgs e)
    {
        if (Results.SelectedItem is not QuickSearchEntry entry) return;
        // A double-click in empty list space must not open a previously selected row.
        if (e is MouseButtonEventArgs mouse && ItemsControl.ContainerFromElement(Results, mouse.OriginalSource as DependencyObject) is null) return;
        Selected = entry;
        DialogResult = true;
    }
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (e.Key == Key.Enter && (SearchBox.IsKeyboardFocusWithin || Results.IsKeyboardFocusWithin))
        { OpenSelected(sender, e); e.Handled = true; }
        else if (e.Key is Key.Up or Key.Down && SearchBox.IsKeyboardFocusWithin && Results.Items.Count > 0)
        {
            Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, Results.Items.Count - 1);
            Results.ScrollIntoView(Results.SelectedItem);
            e.Handled = true;
        }
    }
}
