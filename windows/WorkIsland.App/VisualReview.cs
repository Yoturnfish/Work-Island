using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WorkIsland.Core;
namespace WorkIsland.App;
public partial class IslandWindow
{
    private async Task RunVisualReview()
    {
        var output=smokeOutput ?? Path.Combine(Path.GetDirectoryName(store.Path)!,"visual-review");
        Directory.CreateDirectory(output);var checks=new List<string>();
        void Check(bool condition,string name){if(!condition)throw new InvalidOperationException(name);checks.Add(name);}
        async Task Capture(string name){UpdateLayout();await Task.Delay(380);SaveImage(Path.Combine(output,name+".png"));SaveImage2x(Path.Combine(output,name+"-2x.png"));}
        try
        {
            timer.Stop();focusReturn.Stop();
            var settings=engine.Data.Settings;
            settings.Rules=[new("review-editor.exe","Visual review")];settings.BackgroundStyle="black";settings.BackgroundOpacity=1;
            settings.Language="zh";settings.Accent="green";settings.ProgressStyle="matrix";settings.DurationStyle="hoursMinutes";
            settings.IslandScale=.82;settings.GoalSeconds=14400;settings.DayResetMinutes=305;settings.Paused=false;settings.ReduceMotion=false;
            ApplyScale();InitializeReviewPreferences();
            var now=DateTimeOffset.Now;var day=LogicalDay.Key(now,engine.Zone,305);
            engine.Data.Sessions.Clear();
            engine.Data.Sessions.Add(new(){ApplicationIdentifier="ChatGPT.exe",ApplicationName="ChatGPT",DayKey=day,StartedAt=now.AddHours(-6),EndedAt=now,EffectiveSeconds=13380});
            engine.Data.Sessions.Add(new(){ApplicationIdentifier="Resolve.exe",ApplicationName="DaVinci Resolve",DayKey=day,StartedAt=now.AddHours(-6),EndedAt=now,EffectiveSeconds=3540});
            engine.Data.Sessions.Add(new(){ApplicationIdentifier="review-editor.exe",ApplicationName="Other",DayKey=day,StartedAt=now.AddHours(-6),EndedAt=now,EffectiveSeconds=1440});
            for(int i=1;i<8;i++)
            {
                var date=DateOnly.Parse(day).AddDays(-i);var start=now.AddDays(-i).AddHours(-6);
                engine.Data.Sessions.Add(new(){ApplicationIdentifier="review-editor.exe",ApplicationName="Visual review",DayKey=date.ToString("yyyy-MM-dd"),StartedAt=start,EndedAt=start.AddHours(6),EffectiveSeconds=i==1?3720:i==2?10800:i%3==0?14400:1800});
            }
            engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new("review-editor.exe","Visual review",0));visualState="";Render();
            Check(engine.Progress(now)==1 && GoalProgress.ScanAnimating,"completed tracking still scans");
            await Capture("01-complete-tracking");
            ExpandButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(300);
            await Capture("02-navigation");
            Check(Math.Abs(Width-344*.82)<1 && Math.Abs(Height-92*.82)<1,"all pages retain compact window dimensions");
            OpenSettings(this,new RoutedEventArgs());await Task.Delay(300);PageScroller.ScrollToTop();await Capture("03-settings-top");
            var backPoint=PageBackButton.TranslatePoint(new Point(17,17),this);
            var backHit=InputHitTest(backPoint) as DependencyObject;
            while(backHit is not null && !ReferenceEquals(backHit,PageBackButton))backHit=VisualTreeHelper.GetParent(backHit);
            Check(ReferenceEquals(backHit,PageBackButton),"visible back control owns its painted hit area");
            PageScroller.ScrollToVerticalOffset(60);await Capture("04-settings-background");
            PageScroller.ScrollToVerticalOffset(120);await Capture("05-settings-time-goal");
            PageScroller.ScrollToVerticalOffset(204);await Capture("06-settings-away-reminder");
            PageScroller.ScrollToBottom();await Capture("07-settings-bottom");
            Check(ExitButton.IsVisible && ReferenceEquals(SettingsPage.Children.OfType<FrameworkElement>().Last(i=>i.IsVisible),ExitButton),"exit remains final visible setting");
            OpenHistory(this,new RoutedEventArgs());await Task.Delay(300);await Capture("08-history-list");
            ShowHistoryStatistics();await Capture("09-history-statistics");
            BackToNavigation(null,new RoutedEventArgs());
            Check(historyMode=="list" && ReferenceEquals(activePage,HistoryPage),"statistics returns to history list");
            ShowHistoryDetail(day);await Capture("10-history-detail");
            var row=HistoryRows.Children.OfType<Grid>().Skip(1).First();
            var input=(StackPanel)row.Children[1];var hours=(TextBox)input.Children[0];var minutes=(TextBox)input.Children[2];
            var old=engine.TodaySeconds(now);hours.Text="bad";
            var invalidKey=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),0,Key.Enter){RoutedEvent=Keyboard.KeyDownEvent};
            hours.RaiseEvent(invalidKey);Check(engine.TodaySeconds(now)==old,"invalid history edit preserves previous total");
            hours.Text="4";minutes.Text="15";hours.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),0,Key.Enter){RoutedEvent=Keyboard.KeyDownEvent});
            Check(engine.TodaySeconds(now)==old-13380+15300,"history Enter commits selected application total");
            Check(new AtomicStore(store.Path).Load().Sessions.Where(s=>s.DayKey==day&&s.ApplicationIdentifier=="ChatGPT.exe").Sum(s=>s.EffectiveSeconds)==15300,"history edit is atomically persisted");
            engine.Data.Sessions.First(s=>s.DayKey==day).EffectiveSeconds=13380;
            BackToNavigation(null,new RoutedEventArgs());BackToNavigation(null,new RoutedEventArgs());CollapseToTimer();await Task.Delay(300);
            settings.Paused=false;engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new("unmatched.exe","Unmatched",0));Render();
            await Capture("11-complete-away");
            Check(!GoalProgress.ScanAnimating && !GoalProgress.PulseAnimating && ((ScaleTransform)StatusIcon.RenderTransform).HasAnimatedProperties,"completed away matrix is static; cup continues pulse");
            settings.BackgroundOpacity=.8;ApplyWindowOpacity();NotifyPointer(false);Check(Math.Abs(Opacity-.8)<.001,"outside opacity follows preference");
            NotifyPointer(true);Check(Math.Abs(Opacity-.8)<.001,"hover retains the manually selected opacity");
            await Capture("12-hover-opacity");
            NotifyPointer(false);settings.BackgroundOpacity=1;
            ExpandButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(300);Check(Opacity==1,"expanded page has full opacity");
            BeginFocusReturn();var focusWait=System.Diagnostics.Stopwatch.StartNew();
            while(isExpanded && focusWait.ElapsedMilliseconds<4000)await Task.Delay(25);
            Check(!isExpanded,"focus-loss returns after the one-second timer within dispatcher tolerance");
            ExpandButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(300);BeginFocusReturn();focusReturn.Stop();await Task.Delay(1100);
            Check(isExpanded,"regained focus cancellation retains expanded view");
            CollapseToTimer();await Task.Delay(300);
            settings.BackgroundStyle="glass";ApplyMaterial();Render();await Capture("13-translucent-fallback");
            Check(Opacity==1 && OpacityRow.Visibility==Visibility.Collapsed && TitleLabel.Opacity==1,"translucent fallback keeps away title visible and hides opacity setting");
            var glassColors=OuterBorder.Background is LinearGradientBrush gradient?gradient.GradientStops.Select(stop=>stop.Color).ToArray():new[]{((SolidColorBrush)OuterBorder.Background).Color};
            var expectedGlass=surfaces?.NativeBackdropAvailable==true?Color.FromArgb(1,0,0,0):Color.FromRgb(31,31,31);
            Check(glassColors.All(color=>color==expectedGlass) && AllowsTransparency,"glass uses a compositor backing or solid gray fallback without dimming content");
            settings.BackgroundStyle="black";ApplyMaterial();settings.Paused=true;engine.Observe(DateTimeOffset.Now,new("review-editor.exe","Review",0));Render();await Capture("14-paused-source-derived");
            Check(!GoalProgress.ScanAnimating && !GoalProgress.PulseAnimating,"paused progress remains static");
            settings.Paused=false;settings.Language="en";ApplyLanguage();engine.Observe(DateTimeOffset.Now,new("review-editor.exe","Review",0));Render();await Capture("15-english-source-derived");
            settings.Language="zh";ApplyLanguage();
            engine.Data.Sessions.First(s=>s.DayKey==day).EffectiveSeconds=7200;
            engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new("unmatched.exe","Unmatched",0));elapsedState="";Render();
            Check(!GoalProgress.PulseAnimating,"unfinished away is static before ten seconds");
            awaySince=DateTimeOffset.Now.AddSeconds(-11);Render();Check(GoalProgress.PulseAnimating,"unfinished away breathes after ten seconds");
            await Capture("16-unfinished-away-source-derived");
            settings.SuperReminderEnabled=true;settings.AwayTimerEnabled=true;awaySince=DateTimeOffset.Now.AddSeconds(-61);Render();
            Check(ReminderGlow.HasAnimatedProperties && TimeLabel.Text.StartsWith("01M"),"reminder and transient away clock activate without work credit");
            var before=engine.TodaySeconds(now);await Task.Delay(300);Render();Check(engine.TodaySeconds(now)==before,"away display and reminder do not add effective seconds");
            settings.ReduceMotion=true;visualState="";Render();Check(!ReminderGlow.HasAnimatedProperties && !GoalProgress.PulseAnimating,"reduce motion stops reminder and progress");
            SwitchColorProfile("blue");
            RuleInput.Text="blue-only.exe";AddRule(null,new RoutedEventArgs());
            SwitchColorProfile("green");
            Check(!settings.Rules.Any(r=>r.Identifier=="blue-only.exe"),"color profiles keep independent application rules");
            SwitchColorProfile("blue");
            Check(settings.Rules.Any(r=>r.Identifier=="blue-only.exe"),"color profile restores its rules");
            var summary=new
            {
                passed=true,checks,dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip,width=ActualWidth,height=ActualHeight,exportDpi=192,
                dataFile=store.Path,
                sourceDerivedVariants=new[]{"glass","paused","English","unfinished away","reminder"},
                limitations=new[]{"WPF foreground exports; synthetic isolated records, not the user's work log.","OS-composited acrylic and external shadow are captured separately by --fidelity-review.","Mac reference captures are complete 100% only. Windows native blur and legal system fonts differ from AppKit HUD and SF fonts."}
            };
            File.WriteAllText(Path.Combine(output,"visual-review.json"),JsonSerializer.Serialize(summary,AtomicStore.JsonOptions));
            Persist();await Task.Delay(100);ExitIsland(null,new RoutedEventArgs());
        }
        catch(Exception error){SmokeFailed=true;File.WriteAllText(Path.Combine(output,"visual-review-error.txt"),error.ToString());exiting=true;Close();}
    }
    private void InitializeReviewPreferences()
    {
        loadingPreferences=true;
        foreach(var pair in new (ComboBox picker,string tag)[]{(BackgroundPicker,"black"),(DurationPicker,"hoursMinutes"),(GoalPicker,"4"),(IdlePicker,"30"),(AwayPicker,"false"),(ReminderPicker,"false"),(LanguagePicker,"zh")})
            pair.picker.SelectedItem=pair.picker.Items.Cast<ComboBoxItem>().First(i=>i.Tag?.ToString()==pair.tag);
        OpacitySlider.Value=1;loadingPreferences=false;LoadSettingsInputs();ApplyMaterial();ApplyLanguage();
    }
    private void SaveImage2x(string path)
    {
        UpdateLayout();
        var bitmap=new RenderTargetBitmap(566,152,192,192,PixelFormats.Pbgra32);bitmap.Render(this);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(path);encoder.Save(output);
    }
}
