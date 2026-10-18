using SteamSwitcher.Core.Helpers;
using SteamSwitcher.ViewModels;
using SteamSwitcher.Views.Dialogs;
using SteamSwitcher.Views.Pages;
using System.Windows;
using System.Windows.Input;

namespace SteamSwitcher.Views;

public partial class MainWindow
{
    private readonly List<string> _recentSearchIds = [];
    private bool _quickSearchOpen;

    private void QuickSearchKeyDown(object sender, KeyEventArgs e)
    {
        // Local window event only: never register this combination with Windows.
        if (Keyboard.Modifiers == ModifierKeys.Control && IsActive && IsEnabled)
        {
            if (e.Key == Key.K)
            {
                e.Handled = true;
                ShowQuickSearch();
                return;
            }

            switch (e.Key)
            {
                case Key.D1:
                case Key.NumPad1:
                    NavigateTo(typeof(AccountsPage), "Accounts");
                    e.Handled = true;
                    break;
                case Key.D2:
                case Key.NumPad2:
                    NavigateTo(typeof(GamesPage), "Games");
                    e.Handled = true;
                    break;
                case Key.D3:
                case Key.NumPad3:
                    NavigateTo(typeof(ActivityPage), "Activity");
                    e.Handled = true;
                    break;
                case Key.D4:
                case Key.NumPad4:
                    NavigateTo(typeof(DiagnosticsPage), "Diagnostics");
                    e.Handled = true;
                    break;
                case Key.OemComma:
                    NavigateTo(typeof(SettingsPage), "Settings");
                    e.Handled = true;
                    break;
            }
        }
    }

    private void QuickSearch_Click(object sender, RoutedEventArgs e) => ShowQuickSearch();
    private void ShowQuickSearch()
    {
        if (_quickSearchOpen || !IsEnabled) return;
        _quickSearchOpen = true;
        try
        {
            var accountPage = _pageProvider.GetPage(typeof(AccountsPage)) as AccountsPage;
            var accounts = accountPage?.ViewModel.QuickSearchAccounts ?? [];
            var entries = new List<QuickSearchEntry>
            {
                new("page:Accounts", "Página", "Contas", "Gerenciar contas da Steam", "login perfis"),
                new("page:Games", "Página", "Jogos", "Sua biblioteca instalada", "biblioteca"),
                new("page:Settings", "Página", "Configurações", "Aparência e preferências", "tema atalho aparência"),
                new("page:Diagnostics", "Página", "Diagnóstico da Steam", "Verificar instalações", "erro reparar suporte"),
                new("page:Activity", "Página", "Atividade recente", "Histórico de operações e desempenho", "histórico desempenho")
            };
            entries.AddRange(_gamesViewModel.QuickSearchGames.Select(card => new QuickSearchEntry(
                "game:" + card.Game.UniqueKey, "Jogo", card.Game.Name,
                $"{card.Game.OwnerAccount?.DisplayName ?? "Conta não definida"} · {card.Game.InstallationName} · {card.Game.DriveLetter}",
                card.Game.AppId, card.CoverPath)));
            entries.AddRange(accounts.Select(card => new QuickSearchEntry("account:" + card.Account.UniqueKey,
                "Conta", card.Account.DisplayName, $"@{card.Account.AccountName} · {card.Account.InstallationName}", card.Account.AccountName,
                card.AvatarPath)));
            var dialog = new QuickSearchDialog(entries, _recentSearchIds) { Owner = this };
            var blur = new System.Windows.Media.Effects.BlurEffect
            {
                Radius = 14,
                RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance
            };
            var oldEffect = RootGrid.Effect;
            RootGrid.Effect = blur;
            QuickSearchEntry? selected = null;
            try
            {
                if (dialog.ShowDialog() == true)
                {
                    selected = dialog.Selected;
                }
            }
            finally
            {
                RootGrid.Effect = oldEffect;
            }
            if (selected is null) return;
            var opened = false;
            if (selected.Id.StartsWith("game:", StringComparison.Ordinal))
            {
                opened = NavigateTo(typeof(GamesPage), "Games");
                if (opened)
                {
                    _gamesViewModel.SelectedFilterAccount = _gamesViewModel.FilterAccounts.FirstOrDefault();
                    _gamesViewModel.SearchText = selected.Title;
                    (_pageProvider.GetPage(typeof(GamesPage)) as GamesPage)?.ResetNavigationPosition();
                }
            }
            else if (selected.Id.StartsWith("account:", StringComparison.Ordinal))
            {
                opened = NavigateTo(typeof(AccountsPage), "Accounts");
                if (opened && accountPage is not null)
                {
                    accountPage.ViewModel.ClearAccountFiltersCommand.Execute(null);
                    accountPage.ViewModel.SearchText = accounts.First(card => "account:" + card.Account.UniqueKey == selected.Id).Account.AccountName;
                    accountPage.ResetNavigationPosition();
                }
            }
            else
            {
                opened = selected.Id switch
                {
                    "page:Accounts" => NavigateTo(typeof(AccountsPage), "Accounts"),
                    "page:Games" => NavigateTo(typeof(GamesPage), "Games"),
                    "page:Settings" => NavigateTo(typeof(SettingsPage), "Settings"),
                    "page:Diagnostics" => NavigateTo(typeof(DiagnosticsPage), "Diagnostics"),
                    "page:Activity" => ShowActivity(),
                    _ => false
                };
            }
            if (opened)
            {
                _recentSearchIds.Remove(selected.Id);
                _recentSearchIds.Insert(0, selected.Id);
                if (_recentSearchIds.Count > 10) _recentSearchIds.RemoveAt(10);
            }
        }
        finally { _quickSearchOpen = false; RestoreContentFocus(); }
    }

    private void Activity_Click(object sender, RoutedEventArgs e) => ShowActivity();
    private bool ShowActivity() => NavigateTo(typeof(ActivityPage), "Activity");
    internal void OpenDiagnosticsPage() => NavigateTo(typeof(DiagnosticsPage), "Diagnostics");

    private void RestoreContentFocus()
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
        {
            if (!IsActive || !IsEnabled) return;
            FocusManager.SetFocusedElement(this, MainFrame);
            Keyboard.Focus(MainFrame);
        }));
    }
}
