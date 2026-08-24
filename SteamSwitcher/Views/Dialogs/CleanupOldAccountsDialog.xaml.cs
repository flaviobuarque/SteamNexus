using SteamSwitcher.Core.Models;
using SteamSwitcher.Core.Helpers;
using System.Windows;
using System.Windows.Controls;

namespace SteamSwitcher.Views.Dialogs;

public partial class CleanupOldAccountsDialog : Window
{
    private readonly IReadOnlyList<SteamAccount> _accounts;

    public IReadOnlyList<CleanupPeriod> Periods { get; } = Enumerable.Range(1, 12)
        .Select(months => new CleanupPeriod(
            months,
            months == 1 ? "1 mês" : $"{months} meses"))
        .ToList();

    public int SelectedMonths { get; set; } = 3;
    public IReadOnlyList<SteamAccount> CandidateAccounts { get; private set; } = [];

    public CleanupOldAccountsDialog(IReadOnlyList<SteamAccount> accounts)
    {
        _accounts = accounts;
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) =>
        {
            if (Owner is not null)
            {
                Width = Owner.ActualWidth;
                Height = Owner.ActualHeight;
                Left = Owner.Left;
                Top = Owner.Top;
            }
            UpdateCandidates();
        };
    }

    private void PeriodComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e) => UpdateCandidates();

    private void UpdateCandidates()
    {
        if (CandidateCountText is null || CandidateNamesText is null
            || ConfirmButton is null || ConfirmButtonText is null)
            return;

        if (PeriodComboBox?.SelectedValue is int months)
            SelectedMonths = months;

        CandidateAccounts = AccountCleanupPolicy.GetCandidates(
            _accounts,
            SelectedMonths);

        var count = CandidateAccounts.Count;
        CandidateCountText.Text = count switch
        {
            0 => "Nenhuma conta será removida",
            1 => "1 conta será removida",
            _ => $"{count} contas serão removidas"
        };
        CandidateNamesText.Text = count == 0
            ? "Não há contas inativas anteriores ao período selecionado."
            : string.Join(", ", CandidateAccounts.Take(6).Select(account => account.DisplayName))
                + (count > 6 ? $" e mais {count - 6}" : string.Empty);
        ConfirmButtonText.Text = count == 0
            ? "Nada para apagar"
            : count == 1 ? "Apagar 1 conta" : $"Apagar {count} contas";
        ConfirmButton.IsEnabled = count > 0;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (CandidateAccounts.Count == 0) return;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;

    public sealed record CleanupPeriod(int Months, string Label);
}
