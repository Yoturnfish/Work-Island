using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using WorkIsland.Core;

namespace WorkIsland.App;
public partial class IslandWindow
{
    private Window? fidelityBackground;
    private async Task RunFidelityReview()
    {
        var output = smokeOutput ?? Path.Combine(Path.GetDirectoryName(store.Path)!, "fidelity-review");
        Directory.CreateDirectory(output);
        Window? background = null;
        var checks = new List<object>();
        void Check(string name, bool passed, object? evidence = null)
        {
            checks.Add(new { name, passed, evidence });
            if (!passed) SmokeFailed = true;
        }
        try
        {
            timer.Stop(); focusReturn.Stop();
            var settings = engine.Data.Settings;
            settings.IslandScale = .82; settings.GoalSeconds = 14400; settings.BackgroundOpacity = 1;
            settings.Paused = false; settings.ReduceMotion = false; settings.BackgroundStyle = "black";
            settings.Rules = [new("review.exe", "Review")];
            engine.Data.Sessions.Clear();
            var now = DateTimeOffset.Now;
            engine.Data.Sessions.Add(new() { ApplicationIdentifier = "review.exe", ApplicationName = "Review",
                StartedAt = now.AddHours(-5), EndedAt = now, DayKey = LogicalDay.Key(now, engine.Zone, settings.DayResetMinutes), EffectiveSeconds = 18420 });
            ApplyScale(); InitializeReviewPreferences();
            Left = 300; Top = 250; Topmost = true;
            background = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false, ShowActivated = false, Width = 430, Height = 210, Left = 235, Top = 190,
                Background = Brushes.White, Title = "Work Island isolated visual test background" };
            var pattern = new DrawingGroup();
            using (var drawing = pattern.Open())
            {
                drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(226, 232, 239)), null, new Rect(0, 0, 430, 210));
                for (int x = 0; x < 430; x += 8)
                    drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(60, 100, 160)), null, new Rect(x, 0, 4, 210));
            }
            background.Background = new DrawingBrush(pattern) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
            background.Show(); Activate();
            background.Topmost = true;
            fidelityBackground = background;
            engine.ResetObservation(); engine.Observe(now, new("review.exe", "Review", 0)); visualState = ""; Render();
            await Task.Delay(500);
            SaveScreenRegion(Path.Combine(output, "01-tracking-screen.png"), 23);
            Check("black style has an external shadow", surfaces?.ShadowVisible == true);
            ExpandButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(100); SaveScreenRegion(Path.Combine(output, "02-expand-midpoint-screen.png"), 23);
            await Task.Delay(180); SaveScreenRegion(Path.Combine(output, "03-navigation-screen.png"), 23);
            Check("expand settles with only navigation visible", CompactPanel.Visibility == Visibility.Collapsed && DetailsPanel.Visibility == Visibility.Visible && DetailsPanel.Opacity == 1);
            OpenSettings(this, new RoutedEventArgs()); await Task.Delay(280);
            SaveScreenRegion(Path.Combine(output, "04-settings-screen.png"), 23);
            PageScroller.ScrollToVerticalOffset(LoginCheck.TransformToAncestor(SettingsPage).Transform(new Point()).Y);
            UpdateLayout(); await Task.Delay(100);
            LoginCheck.ApplyTemplate();
            var toggleThumb = (System.Windows.Shapes.Ellipse)LoginCheck.Template.FindName("ToggleThumb", LoginCheck);
            var toggleShift = (TranslateTransform)toggleThumb.RenderTransform;
            LoginCheck.IsChecked = true; await Task.Delay(220);
            var beforeSwitch = toggleShift.X;
            var switchFrames = new List<object>(); var sawIntermediate = false; var switchWatch = Stopwatch.StartNew();
            void SampleSwitch(object? sender, EventArgs args)
            {
                var x = toggleShift.X; switchFrames.Add(new { elapsedMs = switchWatch.Elapsed.TotalMilliseconds, x });
                sawIntermediate |= x > .5 && x < 13.5;
            }
            CompositionTarget.Rendering += SampleSwitch;
            try { LoginCheck.IsChecked = false; await Task.Delay(250); }
            finally { CompositionTarget.Rendering -= SampleSwitch; }
            Check("switch moves through an intermediate position", sawIntermediate, new { beforeSwitch, frames = switchFrames });
            Check("switch settles at 0.18 seconds", Math.Abs(toggleShift.X) < .01);
            LoginCheck.IsChecked = true; await Task.Delay(220);
            PageScroller.ScrollToTop();
            BackToNavigation(null, new RoutedEventArgs());
            Check("back transition retains the outgoing settings shell", Subpage.Visibility == Visibility.Visible && SettingsPage.Visibility == Visibility.Visible);
            await Task.Delay(100); SaveScreenRegion(Path.Combine(output, "05-back-midpoint-screen.png"), 23);
            await Task.Delay(180);
            Check("back settles to navigation and hides shell", Subpage.Visibility == Visibility.Collapsed && NavigationHome.Visibility == Visibility.Visible);
            CollapseToTimer(); await Task.Delay(280);
            Check("compact baseline offset survives round trip", CompactPanel.Children.OfType<StackPanel>().First().RenderTransform is TranslateTransform { Y: -2 });
            for (int i = 0; i < 6; i++) { ToggleExpanded(null, new RoutedEventArgs()); await Task.Delay(35); }
            await Task.Delay(300);
            Check("rapid reversal leaves no faded compact layer", !isExpanded && CompactPanel.Visibility == Visibility.Visible && CompactPanel.Opacity == 1 && DetailsPanel.Visibility == Visibility.Collapsed);
            SaveScreenRegion(Path.Combine(output, "06-collapsed-after-reversal-screen.png"), 23);
            settings.Language = "en"; ApplyLanguage(); Render(); UpdateLayout();
            SaveScreenRegion(Path.Combine(output, "06b-english-screen.png"), 23);
            Check("English compact title respects source minimum scale", TitleLabel.FontSize >= 13 * .65 && TitleLabel.FontSize <= 13);
            settings.DisplayTitle = "Long compact title for narrow layout verification"; Render(); UpdateLayout();
            Check("long title shrinks to the source minimum before truncation", Math.Abs(TitleLabel.FontSize - 13 * .65) < .01);
            settings.DisplayTitle = ""; settings.Language = "zh"; ApplyLanguage(); Render(); UpdateLayout();
            BackgroundPicker.SelectedItem = BackgroundPicker.Items.Cast<ComboBoxItem>().First(i => i.Tag?.ToString() == "glass");
            await Task.Delay(600);
            SaveScreenRegion(Path.Combine(output, "07-glass-screen.png"), 23);
            Check("native host backdrop is enabled on this Windows host", surfaces?.NativeBackdropAvailable == true && surfaces.BackdropVisible,
                new { result = surfaces?.BackdropResult, handle = surfaces?.BackdropHandle.ToInt64(), os = Environment.OSVersion.VersionString });
            Check("transparent style removes external shadow", surfaces?.ShadowVisible == false);
            // High-frequency stripes are beneath the top interior, away from text and orb.
            surfaces?.SetBackdropBlend(1); await Task.Delay(250);
            var opaqueContrast = ScreenStripeContrast();
            Check("host backdrop blur suppresses background stripe contrast", opaqueContrast < 45, new { maximumAdjacentContrast = opaqueContrast });
            var opaqueCool = SampleMaterialColor();
            background.Background = new SolidColorBrush(Color.FromRgb(240, 110, 55));
            PositionTestBackground(); await Task.Delay(500);
            var opaqueWarm = SampleMaterialColor();
            var opaqueResponse = Math.Abs(opaqueCool.R - opaqueWarm.R) + Math.Abs(opaqueCool.G - opaqueWarm.G) + Math.Abs(opaqueCool.B - opaqueWarm.B);
            Check("blurred host brush responds to the background at full blend", opaqueResponse > 15, new { opaqueCool, opaqueWarm, opaqueResponse });
            background.Background = new DrawingBrush(pattern) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
            PositionTestBackground(); await Task.Delay(250);
            surfaces?.SetBackdropBlend(.25); await Task.Delay(250);
            var contrast = ScreenStripeContrast();
            Check("fixed quarter backdrop blend preserves sharp background component", contrast > opaqueContrast + 20 && contrast < 170, new { opaqueContrast, quarterBlendContrast = contrast });
            SaveScreenRegion(Path.Combine(output, "07-glass-screen.png"), 23);
            var cool = SampleMaterialColor();
            background.Background = new SolidColorBrush(Color.FromRgb(240, 110, 55));
            PositionTestBackground(); await Task.Delay(500);
            var warm = SampleMaterialColor();
            var response = Math.Abs(cool.R - warm.R) + Math.Abs(cool.G - warm.G) + Math.Abs(cool.B - warm.B);
            Check("glass responds to the actual background instead of a solid fill", response > 25, new { cool, warm, response });
            SaveScreenRegion(Path.Combine(output, "07b-glass-warm-background-screen.png"), 23);
            background.Background = new DrawingBrush(pattern) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
            PositionTestBackground(); await Task.Delay(250);
            ToggleExpanded(null, new RoutedEventArgs()); await Task.Delay(280); OpenSettings(this, new RoutedEventArgs()); await Task.Delay(280);
            SaveScreenRegion(Path.Combine(output, "08-glass-settings-screen.png"), 23);
            Check("glass page overlay stays inside compact frame", GlassPageWash.Visibility == Visibility.Visible && Opacity == 1);
            Hide(); await Task.Delay(60);
            Check("hiding foreground hides material and shadow", surfaces?.BackdropVisible == false && surfaces.ShadowVisible == false);
            Show(); Activate(); await Task.Delay(100);
            Check("show restores material without expanding frame", surfaces?.BackdropVisible == true && Math.Abs(Width - 344 * .82) < .001);
            BackgroundPicker.SelectedItem = BackgroundPicker.Items.Cast<ComboBoxItem>().First(i => i.Tag?.ToString() == "black");
            CollapseToTimer(); settings.Paused = true; engine.Observe(DateTimeOffset.Now, new("review.exe", "Review", 0)); Render(); await Task.Delay(400);
            SaveScreenRegion(Path.Combine(output, "09-paused-screen.png"), 23);
            settings.Paused = false; engine.ResetObservation(); engine.Observe(DateTimeOffset.Now, new("away.exe", "Away", 0)); Render(); await Task.Delay(400);
            SaveScreenRegion(Path.Combine(output, "10-away-screen.png"), 23);
            settings.ReduceMotion = true; visualState = ""; Render(); ToggleExpanded(null, new RoutedEventArgs());
            Check("reduce motion skips root slide and fade", CompactPanel.Visibility == Visibility.Collapsed && DetailsPanel.Opacity == 1 && !DetailsPanel.HasAnimatedProperties);
        }
        catch (Exception error) { SmokeFailed = true; checks.Add(new { name = "fidelity review", passed = false, error = error.ToString() }); }
        finally
        {
            File.WriteAllText(Path.Combine(output, "fidelity-review.json"), JsonSerializer.Serialize(new
            {
                passed = !SmokeFailed, checks, dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip,
                capture = "OS screen pixels from the application's window region, with an owned synthetic stripe background; no user windows captured.",
                data = store.Path
            }, AtomicStore.JsonOptions));
            fidelityBackground = null; background?.Close(); exiting = true; Close();
        }
    }
    private void SaveScreenRegion(string path, int margin)
    {
        PositionTestBackground();
        if (!WindowsActivity.GetWindowRect(new WindowInteropHelper(this).Handle, out var rect)) throw new InvalidOperationException("Missing live window bounds");
        var dpi = VisualTreeHelper.GetDpi(this); var padding = (int)Math.Ceiling(margin * dpi.DpiScaleX);
        using var bitmap = new System.Drawing.Bitmap(rect.Right - rect.Left + padding * 2, rect.Bottom - rect.Top + padding * 2);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(rect.Left - padding, rect.Top - padding, 0, 0, bitmap.Size);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
    private double ScreenStripeContrast()
    {
        PositionTestBackground();
        WindowsActivity.GetWindowRect(new WindowInteropHelper(this).Handle, out var rect);
        var dpi = VisualTreeHelper.GetDpi(this);
        using var sample = new System.Drawing.Bitmap(120, 1);
        using (var graphics = System.Drawing.Graphics.FromImage(sample))
            graphics.CopyFromScreen(rect.Left + (int)(80 * dpi.DpiScaleX), rect.Top + (int)(8 * dpi.DpiScaleY), 0, 0, sample.Size);
        return Enumerable.Range(1, 119).Max(i =>
        {
            var a = sample.GetPixel(i - 1, 0); var b = sample.GetPixel(i, 0);
            return (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B)) / 3d;
        });
    }
    private void PositionTestBackground()
    {
        if (fidelityBackground is null) return;
        var bottom = surfaces?.ShadowVisible == true ? surfaces.ShadowHandle : surfaces?.BackdropVisible == true ? surfaces.BackdropHandle : new WindowInteropHelper(this).Handle;
        WindowsActivity.SetWindowPos(new WindowInteropHelper(fidelityBackground).Handle, bottom, 0, 0, 0, 0, 0x0013);
        NativeCompositionFlush();
    }
    private System.Drawing.Color SampleMaterialColor()
    {
        WindowsActivity.GetWindowRect(new WindowInteropHelper(this).Handle, out var rect);
        var dpi = VisualTreeHelper.GetDpi(this);
        using var sample = new System.Drawing.Bitmap(1, 1);
        using (var graphics = System.Drawing.Graphics.FromImage(sample))
            graphics.CopyFromScreen(rect.Left + (int)(120 * dpi.DpiScaleX), rect.Top + (int)(8 * dpi.DpiScaleY), 0, 0, sample.Size);
        return sample.GetPixel(0, 0);
    }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll", EntryPoint = "DwmFlush")] private static extern int NativeCompositionFlush();
}

