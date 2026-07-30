using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamSwitcher.Core.Models;
using System.Windows;
using System.Windows.Media.Animation;
using System.Collections.ObjectModel;

namespace SteamSwitcher.Views.Dialogs;

public partial class PickAccountDialog : Window
{
    public SteamAccount? SelectedAccount { get; private set; }
    private readonly PickAccountDialogViewModel _vm;

    public PickAccountDialog(string gameName, IReadOnlyList<SteamAccount> accounts)
    {
        InitializeComponent();
        _vm = new PickAccountDialogViewModel(gameName, accounts);
        DataContext = _vm;
        Loaded += (_, _) =>
        {
            if (Owner is null) return;
            Width = Owner.ActualWidth;
            Height = Owner.ActualHeight;
            Left = Owner.Left;
            Top = Owner.Top;

            // Animação: escala de 0.85 para 1.0 + fade in
            DialogCard.RenderTransformOrigin = new Point(0.5, 0.5);
            var scaleX = new DoubleAnimation(0.85, 1.0,
                new Duration(TimeSpan.FromMilliseconds(180)))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var scaleY = new DoubleAnimation(0.85, 1.0,
                new Duration(TimeSpan.FromMilliseconds(180)))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var fade = new DoubleAnimation(0, 1,
                new Duration(TimeSpan.FromMilliseconds(150)));

            var transform = (System.Windows.Media.ScaleTransform)DialogCard.RenderTransform;
            transform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleX);
            transform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleY);
            DialogCard.BeginAnimation(OpacityProperty, fade);
        };
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedAccount is null) return;
        SelectedAccount = _vm.SelectedAccount;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

public partial class PickAccountDialogViewModel : ObservableObject
{
    public string GameName { get; }
    private readonly IReadOnlyList<SelectableAccount> _allAccounts;

    public ObservableCollection<SelectableAccount> Accounts { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private SelectableAccount? _selectedItem;

    public bool HasSelection => SelectedItem is not null;
    public SteamAccount? SelectedAccount => SelectedItem?.Account;

    public PickAccountDialogViewModel(string gameName, IReadOnlyList<SteamAccount> accounts)
    {
        GameName = gameName;
        _allAccounts = accounts.Select(a => new SelectableAccount(a)).ToList();

        foreach (var account in _allAccounts)
            Accounts.Add(account);
    }

    private void ApplyFilter()
    {
        var filtered = _allAccounts.Where(account =>
            string.IsNullOrWhiteSpace(SearchText) ||
            account.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
            account.AccountName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        Accounts.Clear();

        foreach (var account in filtered)
            Accounts.Add(account);
    }

    [RelayCommand]
    private void SelectAccount(SelectableAccount item)
    {
        if (SelectedItem is not null)
            SelectedItem.IsSelected = false;
        item.IsSelected = true;
        SelectedItem = item;
    }
}

public partial class SelectableAccount(SteamAccount account) : ObservableObject
{
    public SteamAccount Account { get; } = account;
    public string DisplayName => Account.DisplayName;
    public string AccountName => Account.AccountName;

    [ObservableProperty]
    private bool _isSelected;
}