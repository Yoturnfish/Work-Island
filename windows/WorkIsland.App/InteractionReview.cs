using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Text.Json;
using System.Windows.Threading;
using System.Windows.Input;

namespace WorkIsland.App;

// A separate owned test surface makes focus changes observable and keeps screenshot
// backgrounds free of unrelated applications. It never drives the island's controls.
internal static class InteractionReview
{
    public static void Open(IslandWindow island, string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var eventFile = Path.Combine(dataDirectory, "interaction-events.jsonl");
        void Record(string name) => File.AppendAllText(eventFile, JsonSerializer.Serialize(new
        { timestamp = DateTimeOffset.Now, name, state = island.InteractionSnapshot() }) + Environment.NewLine);
        island.PreviewMouseLeftButtonUp += (_, e) =>
        {
            var name = "mouse-up:" + e.OriginalSource.GetType().Name;
            island.Dispatcher.BeginInvoke(() => Record(name), DispatcherPriority.ContextIdle);
        };
        island.PreviewKeyUp += (_, e) => Record("key-up:" + e.Key);
        // Observe real routed input after Button has set IsPressed. This does
        // not synthesize input or hold the button's state for a screenshot.
        void CapturePress(object? sender, RoutedEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            while (source is not null && source is not Button)
                source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
            if (source is Button button && island.CaptureNavigationPressForReview(button, dataDirectory))
                Record("pressed-navigation:" + button.Name);
        }
        island.AddHandler(UIElement.MouseLeftButtonDownEvent, new MouseButtonEventHandler(CapturePress), true);
        island.AddHandler(Keyboard.KeyDownEvent, new KeyEventHandler((sender, e) => { if (e.Key == Key.Space) CapturePress(sender, e); }), true);
        island.Activated += (_, _) => Record("activated");
        island.Deactivated += (_, _) => Record("deactivated");
        island.IsVisibleChanged += (_, _) => Record("visibility");
        island.PreviewMouseRightButtonUp += (_, e) => { e.Handled = true; island.OpenTrayMenuForReview(); Record("native-tray-menu-open"); };
        var sampler = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        string? previous = null;
        sampler.Tick += (_, _) =>
        {
            var snapshot = JsonSerializer.Serialize(island.InteractionSnapshot());
            if (snapshot == previous) return;
            previous = snapshot; Record("state-change");
        };
        sampler.Start();
        var panel = new StackPanel { Margin = new Thickness(32) };
        panel.Children.Add(new TextBlock { Text = "Work Island · 真实交互验收", FontSize = 20, Foreground = Brushes.White });
        panel.Children.Add(new TextBlock { Text = "点击背景可验收失焦返回；右键小岛可验收同一原生托盘菜单。\n小岛使用独立测试数据。", Margin = new Thickness(0, 12, 0, 0), Foreground = Brushes.White });
        panel.Children.Add(new TextBlock { Text = Path.GetFullPath(dataDirectory), Margin = new Thickness(0, 12, 0, 0), Foreground = Brushes.LightGray });
        var backdrop = new Window
        {
            Title = "Work Island · Review background", Width = 950, Height = 600,
            Left = SystemParameters.WorkArea.Left + 60, Top = SystemParameters.WorkArea.Top + 100,
            Background = new SolidColorBrush(Color.FromRgb(39, 51, 65)), Content = panel,
            ShowInTaskbar = true, ShowActivated = false
        };
        var closing = false;
        backdrop.Closing += (_, e) => e.Cancel = !closing;
        backdrop.PreviewMouseRightButtonUp += (_, e) => { e.Handled = true; island.OpenTrayMenuForReview(); Record("background-native-tray-menu-open"); };
        island.Closed += (_, _) => { sampler.Stop(); Record("closed"); closing = true; backdrop.Close(); };
        backdrop.Show();
        island.Left = backdrop.Left + 100; island.Top = backdrop.Top + 180;
        island.Activate();
    }
}

public partial class IslandWindow
{
    internal bool CaptureNavigationPressForReview(Button button, string dataDirectory)
    {
        if (!button.IsPressed || button.Template?.FindName("MacPressedSurface", button) is not Border surface) return false;
        var content = (ContentPresenter)button.Template.FindName("MacNavigationContent", button);
        var label = string.IsNullOrEmpty(button.Name) ? "NavigationBack" : button.Name;
        var output = Path.Combine(dataDirectory, "pressed-navigation");
        Directory.CreateDirectory(output);
        SaveImage2x(Path.Combine(output, label + ".png"));
        File.WriteAllText(Path.Combine(output, label + ".json"), JsonSerializer.Serialize(new
        {
            timestamp = DateTimeOffset.Now, button = label, button.IsPressed,
            surface = (surface.Background as SolidColorBrush)?.Color.ToString(),
            cornerRadius = surface.CornerRadius.TopLeft, foregroundOpacity = content.Opacity,
            width = button.ActualWidth, height = button.ActualHeight,
            method = "Actual routed mouse/Space down; unmodified WPF foreground export while IsPressed is true. Not an OS compositor screenshot."
        }));
        return true;
    }
}
