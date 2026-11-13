using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private sealed record ComplexFrame(string File,double ElapsedSeconds,double CaptureEndSeconds,string State,bool Native,bool StateClock,bool InkClock,double StateDuration,double InkDuration,double Clock,double Coffee,double Pause,byte TitleRed,string TimeColor,bool SemanticTime,bool Expanded,bool Reduced,bool Bright);
    private static async Task RunComplexGlassMotion(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;
        settings.HasCompletedOnboarding=true;settings.DisplayMode="floating";settings.IslandScale=.82;settings.Paused=false;settings.ReduceMotion=false;
        settings.BackgroundStyle="glass";settings.BackgroundOpacity=1;settings.DurationStyle="hoursMinutes";settings.AwayTimerEnabled=false;settings.SuperReminderEnabled=false;
        settings.Rules=[new("complex-motion.exe","Motion fixture")];settings.Websites=[];settings.GoalSeconds=14400;
        var now=DateTimeOffset.Now;engine.Data.Sessions.Clear();engine.Data.Sessions.Add(new(){ApplicationIdentifier="complex-motion.exe",ApplicationName="Motion fixture",DayKey=LogicalDay.Key(now,engine.Zone,settings.DayResetMinutes),StartedAt=now.AddHours(-2),EndedAt=now,EffectiveSeconds=7200});
        Field<DispatcherTimer>("fullscreenTimer").Stop();window.Left=470;window.Top=350;window.Topmost=true;window.ShowInTaskbar=true;window.Title="Work Island · 透明动效采样";
        var background=new Window{Title="Work Island · 自有纹理背景",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Left=360,Top=250,Width=700,Height=300,ShowActivated=false,ShowInTaskbar=false};
        Brush Pattern(bool bright)
        {
            var group=new DrawingGroup{ClipGeometry=new RectangleGeometry(new Rect(0,0,700,300))};
            using(var dc=group.Open())
            {
                dc.DrawRectangle(new SolidColorBrush(bright?Color.FromRgb(229,236,242):Color.FromRgb(16,24,31)),null,new Rect(0,0,700,300));
                for(var x=0;x<700;x+=28)for(var y=0;y<300;y+=28)
                    if((x/28+y/28)%2==0)dc.DrawRectangle(new SolidColorBrush(bright?Color.FromRgb(205,218,230):Color.FromRgb(39,52,66)),null,new Rect(x,y,28,28));
                var pen=new Pen(new SolidColorBrush(bright?Color.FromArgb(90,122,142,162):Color.FromArgb(100,76,94,116)),2);
                for(var x=-300;x<700;x+=37)dc.DrawLine(pen,new Point(x,0),new Point(x+300,300));
            }
            group.Freeze();return new DrawingBrush(group){Stretch=Stretch.Fill};
        }
        var bright=false;background.Background=Pattern(bright);background.Show();typeof(IslandWindow).GetField("fidelityBackground",Flags)!.SetValue(window,background);
        void State(string state){settings.Paused=state=="paused";engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new(state=="tracking"?"complex-motion.exe":"unmatched.exe","Motion fixture",0));Call("Render");}
        Call("ApplyScale");Call("ApplyMaterial");Call("NotifyPointer",false);State("tracking");await Task.Delay(450);window.EnableNativeForeground();window.EnableNativePulses();Call("Render");window.Activate();await Task.Delay(650);
        var motion=Path.Combine(output,"motion");var frames=Path.Combine(motion,"frames");Directory.CreateDirectory(frames);
        var rows=new List<ComplexFrame>();var events=new List<object>();var coverFailures=0;var watch=Stopwatch.StartNew();var step=0;var nextRefresh=1d;
        double? inkSeen=null,pageReverseAt=null;var overlapStep=0;var pageIntermediateFrames=0;
        double Duration(string name)=>Field<Clock?>(name) is {} clock&&clock.Timeline.Duration.HasTimeSpan?clock.Timeline.Duration.TimeSpan.TotalSeconds:0;
        void Event(string name,double planned,Action action){var start=watch.Elapsed.TotalSeconds;action();events.Add(new{name,plannedSeconds=planned,actionStartedSeconds=start,at=watch.Elapsed.TotalSeconds,state=engine.Current.State,native=Field<bool>("nativeForegroundActive")});}
        (double At,string Name,Action Action)[] actions=[
            (.60,"away",()=>State("away")),(.80,"bright-pattern",()=>{bright=true;background.Background=Pattern(true);}),
            (1.30,"tracking",()=>State("tracking")),(4.00,"dark-pattern",()=>{bright=false;background.Background=Pattern(false);}),
            (5.70,"paused",()=>State("paused")),(6.20,"tracking",()=>State("tracking")),
            (6.80,"expand",()=>Call("ToggleExpanded",null,new RoutedEventArgs())),(6.89,"collapse-reverse",()=>Call("CollapseToTimer")),
            (7.40,"expand",()=>Call("ToggleExpanded",null,new RoutedEventArgs())),(7.70,"settings",()=>{Call("OpenSettings",window,new RoutedEventArgs());pageReverseAt=watch.Elapsed.TotalSeconds+.09;}),
            (8.20,"collapse",()=>Call("CollapseToTimer")),
            (8.80,"bright-pattern",()=>{bright=true;background.Background=Pattern(true);}),
            (10.10,"reduce-away",()=>{settings.ReduceMotion=true;State("away");}),(10.55,"reduce-paused",()=>State("paused")),
            (10.90,"reduce-tracking",()=>State("tracking")),(11.20,"resume-motion",()=>{settings.ReduceMotion=false;Call("Render");}),
            (11.80,"paused-full-cycle",()=>State("paused")),(14.00,"tracking",()=>State("tracking")),
            (14.60,"away-full-cycle",()=>State("away")),(20.50,"tracking-final",()=>State("tracking"))];
        try
        {
            while(watch.Elapsed.TotalSeconds<21.5)
            {
                while(step<actions.Length&&watch.Elapsed.TotalSeconds>=actions[step].At){var a=actions[step++];Event(a.Name,a.At,a.Action);}
                if(pageReverseAt is {} reverseAt&&watch.Elapsed.TotalSeconds>=reverseAt){Event("page-reverse",reverseAt,()=>Call("BackToNavigation",null,new RoutedEventArgs()));pageReverseAt=null;}
                // The normal sampler's own 1Hz guard and two-sample hysteresis
                // remain untouched. Arrange a reversal relative to the first
                // real ink clock, rather than fabricating a luminance result.
                if(watch.Elapsed.TotalSeconds>=nextRefresh){Call("Render");nextRefresh=watch.Elapsed.TotalSeconds+1;}
                if(inkSeen is null&&Field<AnimationClock?>("compactInkMotion") is not null){inkSeen=watch.Elapsed.TotalSeconds;events.Add(new{name="natural-ink-start",at=inkSeen});}
                if(inkSeen is {} ink&&overlapStep<3)
                {
                    var offset=overlapStep switch{0=>.10,1=>.18,_=>.65};
                    if(watch.Elapsed.TotalSeconds>=ink+offset){var state=overlapStep switch{0=>"away",1=>"paused",_=>"tracking"};Event("ink-overlap-"+state,ink+offset,()=>State(state));overlapStep++;}
                }
                if(!WindowsActivity.GetWindowRect(new WindowInteropHelper(window).Handle,out var rect)||!WindowsActivity.GetWindowRect(new WindowInteropHelper(background).Handle,out var bg))throw new InvalidOperationException("Own fixture window missing");
                var padding=(int)Math.Ceiling(23*VisualTreeHelper.GetDpi(window).DpiScaleX);
                if(rect.Left-padding<bg.Left||rect.Top-padding<bg.Top||rect.Right+padding>bg.Right||rect.Bottom+padding>bg.Bottom)coverFailures++;
                var at=watch.Elapsed.TotalSeconds;var file=$"frame-{rows.Count:00000}.png";Call("SaveScreenRegion",Path.Combine(frames,file),23);var end=watch.Elapsed.TotalSeconds;
                if(pageReverseAt is not null&&Field<ClockGroup?>("pageTransition") is not null)pageIntermediateFrames++;
                var stateClock=Field<ClockGroup?>("stateTransition");var inkClock=Field<AnimationClock?>("compactInkMotion");
                rows.Add(new(file,at,end,engine.Current.State,Field<bool>("nativeForegroundActive"),stateClock is not null,inkClock is not null,Duration("stateTransition"),Duration("compactInkMotion"),Field<FrameworkElement>("ClockIcon").Opacity,Field<FrameworkElement>("CoffeeIcon").Opacity,Field<FrameworkElement>("PauseIcon").Opacity,Field<SolidColorBrush>("compactTitleInk").Color.R,((SolidColorBrush)Field<TextBlock>("TimeLabel").Foreground).Color.ToString(),ReferenceEquals(Field<TextBlock>("TimeLabel").Foreground,Field<SolidColorBrush>("statusBrush")),Field<bool>("isExpanded"),settings.ReduceMotion,bright));
                await Task.Delay(12);
            }
            check("all recorded regions remain within owned patterned background",coverFailures==0,new{coverFailures});
            check("recording contains all planned actions and natural luminance overlap",step==actions.Length&&inkSeen is not null&&overlapStep==3,new{step,inkSeen,overlapStep});
            check("page reversal follows at least two captured presented intermediate frames",pageIntermediateFrames>=2,new{pageIntermediateFrames});
            check("source capture has enough actual timestamped intermediate frames",rows.Count>=250&&rows.Any(r=>r.Coffee>.05&&r.Coffee<.95),new{frames=rows.Count});
            check("both independent clocks retain Mac 350ms duration",rows.Any(r=>r.StateClock)&&rows.Any(r=>r.InkClock)&&rows.All(r=>(!r.StateClock||Math.Abs(r.StateDuration-.35)<1e-6)&&(!r.InkClock||Math.Abs(r.InkDuration-.35)<1e-6)),null);
            check("native renderer never snapshots an active state or ink fade",rows.All(r=>!r.Native||!r.StateClock&&!r.InkClock),null);
            check("natural bright and dark scenes produce both settled title inks",rows.Any(r=>r.TitleRed==32)&&rows.Any(r=>r.TitleRed==255),null);
            check("time always retains semantic status brush during background changes",rows.All(r=>r.SemanticTime),null);
            check("compact status properties never all disappear during reversals",rows.Where(r=>!r.Expanded).All(r=>r.Clock+r.Coffee+r.Pause>.96),null);
            check("reduced motion settles both finite clocks",rows.Where(r=>r.Reduced).All(r=>!r.StateClock&&!r.InkClock),null);
            check("recording finishes with native tracking and released fade clocks",Field<bool>("nativeForegroundActive")&&Field<ClockGroup?>("stateTransition") is null&&Field<AnimationClock?>("compactInkMotion") is null,new{state=engine.Current.State});
            check("current compact timer retains specified Consolas Bold 14DIP",Field<TextBlock>("TimeLabel").FontFamily.Source=="Consolas"&&Field<TextBlock>("TimeLabel").FontSize==14&&Field<TextBlock>("TimeLabel").FontWeight==FontWeights.Bold,null);
        }
        finally
        {
            File.WriteAllText(Path.Combine(motion,"motion-capture.json"),JsonSerializer.Serialize(new{durationSeconds=watch.Elapsed.TotalSeconds,frameCount=rows.Count,frames=rows,events,appSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),coreSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(TrackingEngine).Assembly.Location))),scope="Actual own HWND desktop pixels over owned two-tone patterned background, ordinary production native foreground/pulses with real 1Hz-guarded contrast sampling. Controlled model state/navigation actions, not native input. Recorded properties follow each read and are not pixel alpha measurements; capture start/end retained. No user-window imagery, real game/browser/session or compositor constant-FPS claim."},AtomicStore.JsonOptions));
            Call("SuspendNativeForeground");typeof(IslandWindow).GetField("fidelityBackground",Flags)!.SetValue(window,null);background.Close();
        }
    }
}
