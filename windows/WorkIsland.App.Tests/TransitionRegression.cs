using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunTransitionChecks(IslandWindow window, Action<string, bool, object?> check)
    {
        T Field<T>(string name) => (T)typeof(IslandWindow).GetField(name, Flags)!.GetValue(window)!;
        void Call(string name, params object?[] arguments) => typeof(IslandWindow).GetMethod(name, Flags)!.Invoke(window, arguments);
        void Toggle() => Call("ToggleExpanded", null, new RoutedEventArgs());
        void Page(string name, bool animate = true) => Call("ShowPage", name, animate);
        double X(FrameworkElement element) => element.RenderTransform.Value.OffsetX;
        var compact = Field<FrameworkElement>("CompactPanel");
        var details = Field<FrameworkElement>("DetailsPanel");
        var home = Field<FrameworkElement>("NavigationHome");
        var shell = Field<FrameworkElement>("Subpage");
        var engine = Field<TrackingEngine>("engine");
        Field<DispatcherTimer>("focusReturn").Stop();
        var samples = new List<(double gapError, double fadeError)>();
        void Sample(object? sender, EventArgs args)
        {
            if (details.Opacity is <= .05 or >= .95 || compact.Visibility != Visibility.Visible) return;
            samples.Add((Math.Abs(X(details) / details.ActualWidth - X(compact) / compact.ActualWidth - 1),
                Math.Abs(details.Opacity - (1 - X(details) / details.ActualWidth))));
        }
        CompositionTarget.Rendering += Sample;
        try { Toggle(); await Task.Delay(300); }
        finally { CompositionTarget.Rendering -= Sample; }
        check("page motion has actual intermediate frames", samples.Count >= 2, new { count = samples.Count });
        check("incoming and outgoing pages share one motion phase", samples.Count > 0 && samples.Max(s => s.gapError) < .025,
            new { maximumError = samples.Count == 0 ? -1 : samples.Max(s => s.gapError) });
        check("page fade and translation share the same phase", samples.Count > 0 && samples.Max(s => s.fadeError) < .025,
            new { maximumError = samples.Count == 0 ? -1 : samples.Max(s => s.fadeError) });

        Toggle(); await Task.Delay(75);
        var before = new[] { X(compact), X(details), compact.Opacity, details.Opacity };
        Toggle();
        var after = new[] { X(compact), X(details), compact.Opacity, details.Opacity };
        check("rapid root reversal retains the currently presented position and alpha",
            Math.Abs(before[0] - after[0]) < 1.5 && Math.Abs(before[1] - after[1]) < 1.5 &&
            Math.Abs(before[2] - after[2]) < .025 && Math.Abs(before[3] - after[3]) < .025, new { before, after });
        await Task.Delay(300);

        Page("settings"); await Task.Delay(65);
        Toggle(); await Task.Delay(65); Toggle(); await Task.Delay(350);
        check("reopening during a subpage transition cannot hide the restored navigation",
            home.Visibility == Visibility.Visible && home.Opacity == 1 && details.Visibility == Visibility.Visible && shell.Visibility == Visibility.Collapsed, null);

        var settings = Field<FrameworkElement>("SettingsPage");
        Page("settings"); await Task.Delay(300);
        Page("home"); await Task.Delay(75);
        var beforePage = new[] { X(home), X(settings), home.Opacity, settings.Opacity };
        Page("settings");
        var afterPage = new[] { X(home), X(settings), home.Opacity, settings.Opacity };
        check("rapid subpage reversal retains the currently presented position and alpha",
            Math.Abs(beforePage[0] - afterPage[0]) < 1.5 && Math.Abs(beforePage[1] - afterPage[1]) < 1.5 &&
            Math.Abs(beforePage[2] - afterPage[2]) < .025 && Math.Abs(beforePage[3] - afterPage[3]) < .025, new { beforePage, afterPage });
        await Task.Delay(300);
        Page("history"); await Task.Delay(35); Page("recognition"); await Task.Delay(35); Page("home"); await Task.Delay(300);
        var pages = new[] { settings, Field<FrameworkElement>("HistoryPage"), Field<FrameworkElement>("RecognitionPage") };
        check("rapid mixed navigation leaves only the requested page without old clocks",
            home.Visibility == Visibility.Visible && home.Opacity == 1 && shell.Visibility == Visibility.Collapsed &&
            pages.All(page => page.Visibility == Visibility.Collapsed && !page.HasAnimatedProperties && !page.RenderTransform.HasAnimatedProperties), null);

        Page("settings", false);
        engine.Data.Settings.ReduceMotion = true;
        Page("home");
        check("reduced-motion back navigation hides the page shell immediately", shell.Visibility == Visibility.Collapsed, null);
        engine.Data.Settings.ReduceMotion = false;
        await Task.Delay(300);
        Call("CollapseToTimer"); await Task.Delay(300);
        check("page clocks are released after settling", !compact.HasAnimatedProperties && !details.HasAnimatedProperties &&
            !compact.RenderTransform.HasAnimatedProperties && !details.RenderTransform.HasAnimatedProperties &&
            compact.Visibility == Visibility.Visible && compact.Opacity == 1 && details.Visibility == Visibility.Collapsed, null);
    }
}
