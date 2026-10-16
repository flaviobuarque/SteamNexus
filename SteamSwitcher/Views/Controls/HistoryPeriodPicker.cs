using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SteamSwitcher.Views.Controls;

/// <summary>A read-only period button with a transactional, bounded two-month calendar.</summary>
public sealed class HistoryPeriodPicker : UserControl
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");
    private readonly Button _trigger;
    private readonly TextBlock _label = new();
    private readonly Popup _popup;
    private readonly StackPanel _body = new();
    private DateTime _minimum = DateTime.Today, _maximum = DateTime.Today, _leftMonth;
    private DateTime? _draftStart, _draftEnd;
    private bool _awaitingEnd;
    private string _draftPreset = "Período personalizado", _preset = "Período personalizado";
    private int _view, _viewSide, _yearPage;
    public DateTime? Start { get; private set; }
    public DateTime? End { get; private set; }
    public event EventHandler? PeriodChanged;

    public HistoryPeriodPicker()
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        // Geometry avoids dependence on an icon-font glyph being installed.
        var icon = new Path { Data = Geometry.Parse("M2,4 L18,4 18,18 2,18 Z M2,8 L18,8 M6,1 L6,6 M14,1 L14,6"),
            Width = 18, Height = 18, Stretch = Stretch.Uniform, StrokeThickness = 1.5, Margin = new Thickness(0,0,10,0) };
        icon.SetResourceReference(Shape.StrokeProperty, "AppTextPrimaryBrush");
        content.Children.Add(icon);
        content.Children.Add(_label);
        content.Children.Add(new TextBlock { Text = "▾", Margin = new Thickness(12,0,0,0) });
        _trigger = Button(content, Open);
        _trigger.Height = 36;
        _trigger.Margin = new Thickness(0);
        _trigger.ToolTip = "Selecionar data ou período";
        System.Windows.Automation.AutomationProperties.SetName(_trigger, "Selecionar data ou período");
        Content = _trigger;
        var border = new Border { Child = _body, Padding = new Thickness(16), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "AppChromeBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "AppBorderBrush");
        _popup = new Popup { Child = border, PlacementTarget = _trigger, Placement = PlacementMode.Bottom,
            StaysOpen = false, AllowsTransparency = true };
        AddLogicalChild(_popup);
        _popup.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Unloaded += (_, _) => _popup.IsOpen = false;
        UpdateLabel();
    }

    public void SetBounds(DateTime? earliest)
    {
        _maximum = DateTime.Today;
        _minimum = earliest?.Date ?? _maximum;
        if (_minimum > _maximum) _minimum = _maximum;
        _trigger.IsEnabled = earliest.HasValue;
    }

    public void Clear()
    {
        Start = End = null;
        _popup.IsOpen = false;
        UpdateLabel();
    }

    private static DateTime Month(DateTime date) => new(date.Year, date.Month, 1);
    private Button Button(object content, Action action)
    {
        var button = new Button { Content = content, Cursor = Cursors.Hand, MinHeight = 28, Margin = new Thickness(2), Padding = new Thickness(8,5,8,5) };
        button.SetResourceReference(StyleProperty, "NexusActionButton");
        button.Click += (_, _) => action();
        return button;
    }
    private void Open()
    {
        if (_popup.IsOpen) { Close(); return; }
        _draftStart = Start; _draftEnd = End; _draftPreset = _preset;
        _awaitingEnd = false; _view = 0;
        _leftMonth = Month(End ?? _maximum).AddMonths(-1);
        if (_leftMonth < Month(_minimum)) _leftMonth = Month(_minimum);
        Render();
        _popup.IsOpen = true;
        _body.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }
    private void Close() { _popup.IsOpen = false; _trigger.Focus(); }
    private void UpdateLabel()
    {
        _label.Text = Start is not { } start ? "Todo o histórico" : start == End
            ? $"{_preset}: {start.ToString("dd 'de' MMM 'de' yyyy", Culture)}"
            : $"{_preset}: {start.ToString("dd/MM/yyyy", Culture)} a {End:dd/MM/yyyy}";
    }
    private void Preset(string name, int days, bool yesterdayOnly = false)
    {
        _draftStart = _maximum.AddDays(-days);
        if (_draftStart < _minimum) _draftStart = _minimum;
        _draftEnd = yesterdayOnly ? _maximum.AddDays(-1) : _maximum;
        _draftPreset = name; _awaitingEnd = false; _view = 0;
        _leftMonth = Month(_draftEnd.Value).AddMonths(-1);
        if (_leftMonth < Month(_minimum)) _leftMonth = Month(_minimum);
        Render();
    }
    private void SelectDay(DateTime date)
    {
        if (!_awaitingEnd) { _draftStart = _draftEnd = date; _awaitingEnd = true; }
        else
        {
            var first = _draftStart!.Value;
            _draftStart = date < first ? date : first;
            _draftEnd = date > first ? date : first;
            _awaitingEnd = false;
        }
        _draftPreset = "Período personalizado";
        Render();
    }
    private void Render()
    {
        var focused = Keyboard.FocusedElement as Button;
        var focusName = focused == null ? null : System.Windows.Automation.AutomationProperties.GetName(focused);
        _body.Children.Clear();
        var main = new StackPanel { Orientation = Orientation.Horizontal };
        var presets = new StackPanel { Width = 145, Margin = new Thickness(0,0,12,0) };
        presets.Children.Add(new TextBlock { Text = "Períodos", FontWeight = FontWeights.SemiBold, Margin = new Thickness(6,0,0,10) });
        foreach (var (name, days, yesterday) in new[] { ("Hoje",0,false), ("Ontem",1,true), ("Hoje e ontem",1,false), ("Últimos 7 dias",6,false) })
        {
            var b = Button(name, () => Preset(name, days, yesterday));
            b.IsEnabled = !yesterday || _minimum <= _maximum.AddDays(-1);
            if (_draftStart.HasValue && _draftPreset == name) b.SetResourceReference(BackgroundProperty, "AppAccentBrush");
            presets.Children.Add(b);
        }
        main.Children.Add(presets);
        main.Children.Add(BuildMonth(0)); main.Children.Add(BuildMonth(1));
        _body.Children.Add(main);
        var status = new TextBlock { Margin = new Thickness(4,12,4,8), Text = _draftStart.HasValue
            ? $"{_draftStart:dd/MM/yyyy} — {_draftEnd:dd/MM/yyyy}" + (_awaitingEnd ? " · Selecione o fim ou aplique este dia." : "")
            : "Selecione um dia ou o início e o fim do período." };
        status.SetResourceReference(TextBlock.ForegroundProperty, "AppTextSecondaryBrush");
        _body.Children.Add(status);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        footer.Children.Add(Button("Cancelar", Close));
        var apply = Button("Aplicar", () => {
            Start = _draftStart; End = _draftEnd; _preset = _draftPreset;
            UpdateLabel(); Close(); PeriodChanged?.Invoke(this, EventArgs.Empty);
        });
        apply.IsEnabled = _draftStart.HasValue;
        apply.SetResourceReference(StyleProperty, "NexusPrimaryButton");
        footer.Children.Add(apply); _body.Children.Add(footer);
        if (!string.IsNullOrEmpty(focusName))
            FocusDay(_body, focusName);
    }
    private static void FocusDay(DependencyObject parent, string name)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is Button button && System.Windows.Automation.AutomationProperties.GetName(button) == name)
                button.Focus();
            else FocusDay(child, name);
        }
    }
    private FrameworkElement BuildMonth(int side)
    {
        var date = _leftMonth.AddMonths(side);
        var panel = new StackPanel { Width = 258, Margin = new Thickness(4,0,4,0) };
        var header = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var previous = Button("‹", () => { _leftMonth = _leftMonth.AddMonths(-1); _view = 0; Render(); });
        previous.IsEnabled = _leftMonth > Month(_minimum); previous.ToolTip = "Mês anterior";
        var next = Button("›", () => { _leftMonth = _leftMonth.AddMonths(1); _view = 0; Render(); });
        next.IsEnabled = _leftMonth.AddMonths(1) < Month(_maximum); next.ToolTip = "Próximo mês";
        if (side == 0) header.Children.Add(previous);
        header.Children.Add(Button(date.ToString("MMMM", Culture), () => { _view = 1; _viewSide = side; Render(); }));
        header.Children.Add(Button(date.Year.ToString(), () => { _view = 2; _viewSide = side; _yearPage = Math.Max(_minimum.Year, date.Year - 5); Render(); }));
        if (side == 1) header.Children.Add(next);
        panel.Children.Add(header);
        var grid = new UniformGrid { Columns = 7, Rows = 7, Height = 238 };
        if (_view != 0 && _viewSide == side)
        {
            grid.Columns = 3; grid.Rows = 4;
            if (_view == 2)
            {
                var nav = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                var back = Button("‹", () => { _yearPage = Math.Max(_minimum.Year, _yearPage - 12); Render(); });
                back.IsEnabled = _yearPage > _minimum.Year;
                var forward = Button("›", () => { _yearPage += 12; Render(); });
                forward.IsEnabled = _yearPage + 12 <= _maximum.Year;
                nav.Children.Add(back); nav.Children.Add(forward); panel.Children.Add(nav); grid.Height -= 36;
            }
            for (int i = 0; i < 12; i++)
            {
                int value = _view == 1 ? i + 1 : _yearPage + i;
                bool months = _view == 1;
                if (!months && value > _maximum.Year) { grid.Children.Add(new Border()); continue; }
                var target = months ? new DateTime(date.Year, value, 1) : new DateTime(value, date.Month, 1);
                var b = Button(months ? Culture.DateTimeFormat.GetAbbreviatedMonthName(value) : value.ToString(), () => {
                    var selected = target < Month(_minimum) ? Month(_minimum) : target > Month(_maximum) ? Month(_maximum) : target;
                    _leftMonth = selected.AddMonths(-side);
                    if (_leftMonth < Month(_minimum)) _leftMonth = Month(_minimum);
                    _view = 0; Render();
                });
                b.IsEnabled = months ? target >= Month(_minimum) && target <= Month(_maximum) : value >= _minimum.Year && value <= _maximum.Year;
                if (months ? value == date.Month : value == date.Year) b.SetResourceReference(BackgroundProperty, "AppAccentBrush");
                if (months ? date.Year == _maximum.Year && value == _maximum.Month : value == _maximum.Year) b.SetResourceReference(BorderBrushProperty, "AppAccentBrush");
                grid.Children.Add(b);
            }
        }
        else
        {
            foreach (var name in new[] { "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb", "Dom" })
                grid.Children.Add(new TextBlock { Text = name, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 });
            int offset = ((int)date.DayOfWeek + 6) % 7;
            for (int i = 0; i < 42; i++)
            {
                int day = i - offset + 1;
                if (day < 1 || day > DateTime.DaysInMonth(date.Year, date.Month)) { grid.Children.Add(new Border()); continue; }
                var value = date.AddDays(day - 1);
                var b = Button(day.ToString(), () => SelectDay(value)); b.Padding = new Thickness(0);
                b.IsEnabled = value >= _minimum && value <= _maximum;
                System.Windows.Automation.AutomationProperties.SetName(b, value.ToString("D", Culture));
                if (value >= _draftStart && value <= _draftEnd)
                {
                    b.SetResourceReference(BackgroundProperty, "AppAccentBrush");
                    b.Opacity = value == _draftStart || value == _draftEnd ? 1 : .65;
                }
                if (value == _maximum) b.SetResourceReference(BorderBrushProperty, "AppAccentBrush");
                grid.Children.Add(b);
            }
        }
        panel.Children.Add(grid); return panel;
    }
}
