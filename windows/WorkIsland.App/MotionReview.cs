using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WorkIsland.Core;

namespace WorkIsland.App;

public partial class IslandWindow
{
    private async Task RunMotionReview()
    {
        var output = smokeOutput ?? Path.Combine(Path.GetDirectoryName(store.Path)!, "motion-review");
        var framesDirectory = Path.Combine(output, "frames");
        Directory.CreateDirectory(framesDirectory);
        var frames = new List<object>();
        var events = new List<object>();
        Window? background = null;
        try
        {
            timer.Stop(); focusReturn.Stop();
            var settings = engine.Data.Settings;
            settings.IslandScale = .82; settings.GoalSeconds = 14400; settings.DayResetMinutes = 0;
            settings.BackgroundOpacity = 1; settings.Paused = false; settings.ReduceMotion = false;
            settings.BackgroundStyle = "black"; settings.Language = "zh"; settings.DisplayTitle = "";
            settings.Accent = "green"; settings.ProgressStyle = "matrix";
            settings.Rules = [new("motion-review.exe", "Motion review")];
            engine.Data.Sessions.Clear();
            var now = DateTimeOffset.Now;
            engine.Data.Sessions.Add(new() { ApplicationIdentifier = "motion-review.exe", ApplicationName = "Motion review",
                StartedAt = now.AddHours(-5), EndedAt = now, DayKey = LogicalDay.Key(now, engine.Zone, 0), EffectiveSeconds = 18420 });
            ApplyScale(); InitializeReviewPreferences(); Left = 300; Top = 250; Topmost = true;settings.WindowX=Left;settings.WindowY=Top;
            background = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false, ShowActivated = false, Topmost = true, Width = 470, Height = 230,
                Left = 210, Top = 180, Background = new SolidColorBrush(Color.FromRgb(67, 86, 104)),
                Title = "Work Island isolated recording background" };
            background.Show(); Activate(); fidelityBackground = background;
            engine.ResetObservation(); engine.Observe(now, new("motion-review.exe", "Motion review", 0)); visualState = ""; Render();
            await Task.Delay(500);
            var watch = Stopwatch.StartNew();
            var index = 0; var nextAction = 0;
            (double At, string Name, Action Action)[] script =
            [
                (1, "expand navigation", () => ToggleExpanded(null, new RoutedEventArgs())),
                (1.08, "reverse during expansion", () => ToggleExpanded(null, new RoutedEventArgs())),
                (1.16, "reopen while moving", () => ToggleExpanded(null, new RoutedEventArgs())),
                (2.5, "open settings", () => OpenSettings(this, new RoutedEventArgs())),
                (2.57, "collapse during subpage transition", CollapseToTimer),
                (2.64, "reopen during subpage transition", () => ToggleExpanded(null, new RoutedEventArgs())),
                (3.1, "open settled settings", () => OpenSettings(this, new RoutedEventArgs())),
                (4, "scroll settings", () => PageScroller.ScrollToVerticalOffset(60)),
                (5.5, "back to navigation", () => BackToNavigation(null, new RoutedEventArgs())),
                (5.58, "reverse back transition", () => OpenSettings(this, new RoutedEventArgs())),
                (5.66, "return home while moving", () => BackToNavigation(null, new RoutedEventArgs())),
                (7, "collapse to timer", CollapseToTimer),
                (8.2, "clock to coffee crossfade", () => { engine.Observe(DateTimeOffset.Now,new("unmatched.exe","Unmatched",0));Render(); }),
                (9.1, "return to clock", () => { engine.Observe(DateTimeOffset.Now,new("motion-review.exe","Motion review",0));Render(); }),
                (9.19, "reverse icon transition", () => { engine.Observe(DateTimeOffset.Now,new("unmatched.exe","Unmatched",0));Render(); }),
                (9.28, "retarget to clock again", () => { engine.Observe(DateTimeOffset.Now,new("motion-review.exe","Motion review",0));Render(); }),
                (10.5, "pause", () => { settings.Paused = true; engine.Observe(DateTimeOffset.Now, new("motion-review.exe", "Motion review", 0)); Render(); }),
                (12, "resume with icon crossfade", () => { settings.Paused=false;engine.Observe(DateTimeOffset.Now,new("motion-review.exe","Motion review",0));Render(); }),
                (13, "glass away", () => { settings.Paused = false; settings.BackgroundStyle = "glass"; engine.Observe(DateTimeOffset.Now, new("unmatched.exe", "Unmatched", 0)); ApplyMaterial(); Render(); }),
                (16, "onboarding enter .38s", BeginOnboarding),
                (16.12, "onboarding goal during entry fade", () => MoveOnboarding(1)),
                (16.20, "onboarding reverse to language", () => MoveOnboarding(-1)),
                (16.28, "onboarding reverse back to goal", () => MoveOnboarding(1)),
                (18, "onboarding recognition", () => MoveOnboarding(1)),
                (19, "onboarding tutorial", () => MoveOnboarding(1)),
                (20, "onboarding complete", () => MoveOnboarding(1)),
                (21, "onboarding finish .38s scale and fade", FinishOnboarding)
            ];
            // Capture real system pixels; timing is measured, not assumed to be exactly 50 fps.
            while (watch.Elapsed.TotalSeconds < 23)
            {
                while (nextAction < script.Length && watch.Elapsed.TotalSeconds >= script[nextAction].At)
                {
                    var item = script[nextAction++]; item.Action();
                    events.Add(new { elapsedSeconds = watch.Elapsed.TotalSeconds, name = item.Name });
                }
                var elapsed = watch.Elapsed.TotalSeconds;
                var filename = $"frame-{index++:00000}.png";
                SaveScreenRegion(Path.Combine(framesDirectory, filename), 23);
                frames.Add(new { file = filename, elapsedSeconds = elapsed });
                await Task.Delay(20);
            }
            WindowsActivity.GetWindowRect(new WindowInteropHelper(this).Handle, out var rect);
            var dpi = VisualTreeHelper.GetDpi(this);
            File.WriteAllText(Path.Combine(output, "motion-capture.json"), JsonSerializer.Serialize(new
            {
                passed = frames.Count > 300 && nextAction == script.Length,
                version = typeof(IslandWindow).Assembly.GetName().Version!.ToString(),
                durationSeconds = watch.Elapsed.TotalSeconds, frameCount = frames.Count, dpi = dpi.PixelsPerDip,
                physicalWindow = new { width = rect.Right - rect.Left, height = rect.Bottom - rect.Top },
                paddingDip = 23, frames, events,
                method = "GDI CopyFromScreen of this app's real window and shadow; independent solid background; measured capture timestamps",
                limitation = "Scripted app actions and synthetic completed-day data; no mouse pointer recording, audio, time scaling or Mac glass reference. Sampling rate depends on compositor and PNG write time."
            }, AtomicStore.JsonOptions));
            SmokeFailed = frames.Count <= 300 || nextAction != script.Length;
            engine.ResetObservation(); Persist();
        }
        catch (Exception error)
        { SmokeFailed = true; File.WriteAllText(Path.Combine(output, "motion-error.txt"), error.ToString()); }
        finally { fidelityBackground = null; background?.Close(); exiting = true; Close(); }
    }
}
