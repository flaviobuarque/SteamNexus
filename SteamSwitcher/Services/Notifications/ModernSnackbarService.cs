using System.Windows;
using System.Windows.Controls;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SteamSwitcher.Services.Notifications;

public sealed class ModernSnackbarService : ISnackbarService
{
    private SnackbarPresenter? _presenter;
    private Snackbar? _snackbar;

    public TimeSpan DefaultTimeOut { get; set; } = TimeSpan.FromSeconds(5);

    public void SetSnackbarPresenter(SnackbarPresenter contentPresenter)
        => _presenter = contentPresenter;

    public SnackbarPresenter? GetSnackbarPresenter()
        => _presenter;

    public void Show(
        string title,
        string message,
        ControlAppearance appearance,
        IconElement? icon,
        TimeSpan timeout)
    {
        if (_presenter is null)
            throw new InvalidOperationException("O apresentador de avisos ainda não foi configurado.");

        _snackbar ??= CreateSnackbar(_presenter);
        _snackbar.SetCurrentValue(Snackbar.TitleProperty, title);
        _snackbar.SetCurrentValue(ContentControl.ContentProperty, message);
        _snackbar.SetCurrentValue(Snackbar.AppearanceProperty, appearance);
        _snackbar.SetCurrentValue(Snackbar.IconProperty, icon ?? CreateIcon(appearance));
        _snackbar.SetCurrentValue(
            Snackbar.TimeoutProperty,
            timeout == TimeSpan.Zero ? DefaultTimeOut : timeout);
        _snackbar.Show(true);
    }

    private static Snackbar CreateSnackbar(SnackbarPresenter presenter)
    {
        var snackbar = new Snackbar(presenter);
        if (Application.Current.TryFindResource("ModernSnackbarStyle") is Style style)
            snackbar.Style = style;
        return snackbar;
    }

    private static IconElement CreateIcon(ControlAppearance appearance)
        => new SymbolIcon
        {
            Symbol = appearance switch
            {
                ControlAppearance.Success => SymbolRegular.CheckmarkCircle24,
                ControlAppearance.Danger => SymbolRegular.ErrorCircle24,
                ControlAppearance.Caution => SymbolRegular.Warning24,
                _ => SymbolRegular.Info24
            }
        };
}
