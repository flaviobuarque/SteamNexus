using FluentAssertions;
using SteamSwitcher.Core.Helpers;
using SteamSwitcher.Views.Dialogs;
using SteamSwitcher.Views.Pages;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace SteamSwitcher.Tests;

public sealed class InteractionUiTests
{
    [Fact]
    public async Task SharedStylesAndDialogs_LoadAndFollowChangedTheme_WithoutStartingSteam()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                // Plain WPF application only; never invoke SteamSwitcher.App startup.
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri("pack://application:,,,/SteamSwitcher;component/Themes/Dark.xaml") });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri("pack://application:,,,/SteamSwitcher;component/Themes/InteractionStyles.xaml") });
                var button = new Button { Style = (Style)app.FindResource("NexusPrimaryButton"), Content = "Teste" };
                var host = new Window { Content = button };
                button.ApplyTemplate();
                ((SolidColorBrush)button.Background).Color.Should().Be(((SolidColorBrush)app.FindResource("AppAccentBrush")).Color);
                app.Resources["AppAccentBrush"] = new SolidColorBrush(Colors.Purple);
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                ((SolidColorBrush)button.Background).Color.Should().Be(Colors.Purple);

                var confirm = new ConfirmDialog("Confirmar", new string('a', 1000));
                var quick = new QuickSearchDialog([new QuickSearchEntry("id", "Jogo", "Teste", "Conta · E:", "")], []);
                var activity = new ActivityPage();
                var results = (ListBox)quick.FindName("Results");
                results.ItemsSource = Enumerable.Range(0, 20)
                    .Select(i => new QuickSearchEntry(i.ToString(), "Jogo", $"Jogo {i}", "Conta · E:", "")).ToArray();
                foreach (var window in new Window[] { confirm, quick, new MonitoringDialog() })
                {
                    var root = (FrameworkElement)window.Content;
                    root.Measure(new Size(620, 440));
                    root.Arrange(new Rect(0, 0, 620, 440));
                    root.UpdateLayout();
                    root.ActualWidth.Should().BeLessOrEqualTo(620);
                    if (window == quick)
                    {
                        var scroll = (ScrollViewer)results.Template.FindName("PART_ScrollViewer", results);
                        scroll.ScrollableHeight.Should().BeGreaterThan(0);
                        scroll.ScrollToEnd();
                        root.UpdateLayout();
                        scroll.VerticalOffset.Should().BeApproximately(scroll.ScrollableHeight, 1);
                        var last = (ListBoxItem)results.ItemContainerGenerator.ContainerFromIndex(19);
                        var bottom = last.TransformToAncestor(scroll).Transform(new Point(0, last.ActualHeight)).Y;
                        bottom.Should().BeApproximately(scroll.ViewportHeight, 2,
                            "the last result must finish at the viewport bottom, with no trailing scroll area");
                    }
                    window.Close();
                }
                activity.Measure(new Size(900, 500));
                activity.Arrange(new Rect(0, 0, 900, 500));
                activity.UpdateLayout();
                activity.ActualWidth.Should().Be(900);
                ((Button)activity.FindName("NewActivity")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var history = (ListBox)activity.FindName("Entries");
#if DEBUG
                history.Items.Count.Should().Be(30);
                activity.UpdateLayout();
                IEnumerable<Expander> Expanders(DependencyObject root)
                {
                    for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                    {
                        var child = VisualTreeHelper.GetChild(root, i);
                        if (child is Expander expander) yield return expander;
                        foreach (var nested in Expanders(child)) yield return nested;
                    }
                }
                var cards = Expanders(history).ToArray();
                cards.Should().NotBeEmpty();
                cards.Should().OnlyContain(card => !card.IsExpanded);
                var opened = cards[0];
                opened.IsExpanded = true;
                typeof(SteamSwitcher.Views.Pages.ActivityPage)
                    .GetMethod("AddBatch", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(activity, null);
                history.Items.Count.Should().Be(60);
                activity.UpdateLayout();
                Expanders(history).Should().Contain(opened);
                opened.IsExpanded.Should().BeTrue();
                typeof(SteamSwitcher.Views.Pages.ActivityPage)
                    .GetMethod("AddBatch", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(activity, null);
                history.Items.Count.Should().Be(90);
                ((ComboBox)activity.FindName("TypeFilter")).SelectedIndex = 5;
                history.Items.Count.Should().Be(22);
                activity.UpdateLayout();
                Expanders(history).Should().OnlyContain(card => !card.IsExpanded);
                history.Items.Cast<SteamSwitcher.Core.Services.ActivityEntry>().Should()
                    .OnlyContain(e => e.Kind == SteamSwitcher.Core.Services.ActivityKind.SettingsRecovered);
#endif
                ((Button)activity.FindName("ClearFiltersButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                ((Button)activity.FindName("ClearFiltersButton")).Visibility.Should().Be(Visibility.Collapsed);
                var period = (SteamSwitcher.Views.Controls.HistoryPeriodPicker)activity.FindName("PeriodPicker");
                period.SetBounds(DateTime.Today.AddDays(-50));
                period.Content.Should().BeOfType<Button>();
                period.Start.Should().BeNull();
                var trigger = (Button)period.Content;
                trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var popup = (System.Windows.Controls.Primitives.Popup)typeof(SteamSwitcher.Views.Controls.HistoryPeriodPicker)
                    .GetField("_popup", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(period)!;
                IEnumerable<Button> Buttons(DependencyObject root)
                {
                    foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
                    {
                        if (child is Button b) yield return b;
                        foreach (var nested in Buttons(child)) yield return nested;
                    }
                }
                void Click(string text) => Buttons(popup.Child).Single(b => Equals(b.Content, text))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Click("Últimos 7 dias");
                period.Start.Should().BeNull("selection remains pending until Apply");
                Click("Cancelar");
                period.Start.Should().BeNull();
                trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Click("Hoje"); Click("Aplicar");
                period.Start.Should().Be(DateTime.Today);
                period.End.Should().Be(DateTime.Today);
                ((Button)activity.FindName("ClearFiltersButton")).Visibility.Should().Be(Visibility.Visible);
                period.Clear();
                trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Click("Últimos 7 dias"); Click("Aplicar");
                period.Start.Should().Be(DateTime.Today.AddDays(-6));
                period.End.Should().Be(DateTime.Today);
                period.Clear();
                trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                void ClickDay(DateTime day) => Buttons(popup.Child).Single(b =>
                    System.Windows.Automation.AutomationProperties.GetName(b) == day.ToString("D", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                ClickDay(monthStart);
                ClickDay(monthStart.AddDays(-1));
                Click("Aplicar");
                period.Start.Should().Be(monthStart.AddDays(-1), "reverse selections are normalized across months");
                period.End.Should().Be(monthStart);
                period.Clear();
                period.SetBounds(DateTime.Today);
                trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Buttons(popup.Child).Single(b => Equals(b.Content, "Ontem")).IsEnabled.Should().BeFalse();
                var selectableDays = Buttons(popup.Child).Where(b => b.IsEnabled && !string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(b))).ToArray();
                selectableDays.Should().ContainSingle("only today is within the available history bounds");
                Click("Cancelar");
                app.Shutdown();
                completion.SetResult();
            }
            catch (Exception exception) { completion.SetException(exception); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
