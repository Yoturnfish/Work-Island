using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunReminderScheduleChecks(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        void Set(string name,object? value)=>typeof(IslandWindow).GetField(name,Flags)!.SetValue(window,value);
        object? Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|System.Reflection.BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;var glow=Field<FrameworkElement>("ReminderGlow");
        var samples=new List<object>();
        AnimationClock? Clock()=>Field<AnimationClock?>("reminderClock");
        bool FinitePreview()=>Clock()?.Timeline.Duration is {HasTimeSpan:true} duration&&duration.TimeSpan.TotalSeconds is >1.45 and <=1.5;
        void Select(bool enabled){var picker=Field<ComboBox>("ReminderPicker");picker.SelectedItem=picker.Items.Cast<ComboBoxItem>().Single(i=>i.Tag?.ToString()==(enabled?"true":"false"));}
        void State(string state)
        {
            settings.Paused=state=="paused";engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new(state=="tracking"?"reminder-fixture.exe":null,null,0));Call("Render");
        }
        void Record(string phase)=>samples.Add(new{phase,engine.Current.State,glow.Opacity,animated=glow.HasAnimatedProperties,clock=Clock()!=null,finite=Clock()?.Timeline.Duration.HasTimeSpan==true?Clock()!.Timeline.Duration.TimeSpan.TotalSeconds:(double?)null,awaySince=Field<DateTimeOffset?>("awaySince")});
        settings.HasCompletedOnboarding=true;settings.ReduceMotion=false;settings.Rules=[new("reminder-fixture.exe","Reminder fixture")];settings.Websites=[];settings.BackgroundStyle="black";settings.GoalSeconds=14400;settings.AwayThreshold=30;settings.SuperReminderEnabled=false;engine.Data.Sessions.Clear();
        Select(false);State("paused");
        Select(true);await Task.Delay(750);Record("paused-preview-peak");
        check("enabling Flash while paused previews one visible pulse",Clock()!=null&&glow.HasAnimatedProperties&&glow.Opacity>.8,new{glow.Opacity,state=engine.Current.State});
        check("preview owns a finite 1.5 second animation",FinitePreview(),null);
        Select(false);check("Off immediately removes paused preview and releases its clock",Clock()==null&&!glow.HasAnimatedProperties&&glow.Opacity==0,null);
        State("tracking");Select(true);await Task.Delay(750);Record("tracking-preview-peak");
        check("enabling Flash while tracking previews independently of away state",engine.Current.State=="tracking"&&glow.Opacity>.8&&Clock()!=null,new{engine.Current.State,glow.Opacity});
        await Task.Delay(950);Record("tracking-preview-ended");
        check("completed preview releases its clock without needing another render",Clock()==null&&!glow.HasAnimatedProperties&&glow.Opacity==0,null);
        Select(false);State("away");var oldAway=DateTimeOffset.Now.AddSeconds(-125);Set("awaySince",(DateTimeOffset?)oldAway);Call("Render");Select(true);await Task.Delay(100);Record("overdue-away-enabled");
        check("enabling during old away time starts the same finite preview",FinitePreview(),null);
        check("reminder schedule reset does not reset displayed away duration",Field<DateTimeOffset?>("awaySince")==oldAway,null);
        await Task.Delay(1550);Call("Render");Record("away-preview-ended");
        check("after enable preview old away age cannot immediately trigger regular flashing",Clock()==null&&!glow.HasAnimatedProperties&&glow.Opacity==0,new{glow.Opacity,clock=Clock()!=null});
        Select(false);Select(true);await Task.Delay(80);Select(false);Select(true);await Task.Delay(1650);Call("Render");
        check("rapid Off Flash Off Flash cannot leave a stale preview clock",Clock()==null&&!glow.HasAnimatedProperties,null);
        settings.SuperReminderEnabled=true;Set("awaySince",(DateTimeOffset?)DateTimeOffset.Now.AddSeconds(-125));Call("BeginOnboarding");await Task.Delay(400);Record("onboarding");
        check("onboarding suppresses regular reminder and all preview clocks",Clock()==null&&!glow.HasAnimatedProperties&&glow.Opacity==0,null);
        for(var i=0;i<4;i++)Call("MoveOnboarding",1);
        Call("FinishOnboarding");await Task.Delay(450);Call("Render");Record("onboarding-finished");
        check("finishing onboarding starts a fresh away observation instead of overdue age",Field<DateTimeOffset?>("awaySince") is {} fresh&&(DateTimeOffset.Now-fresh).TotalSeconds<2,null);
        check("finishing onboarding does not flash the old away schedule",Clock()==null&&!glow.HasAnimatedProperties&&glow.Opacity==0,null);
        Select(false);settings.ReduceMotion=true;Select(true);await Task.Delay(50);
        check("reduced motion suppresses selection preview",Clock()==null&&!glow.HasAnimatedProperties,null);
        Select(false);settings.ReduceMotion=false;State("away");Select(true);await Task.Delay(50);window.Hide();Call("Render");
        check("hidden window releases preview animation",Clock()==null&&!glow.HasAnimatedProperties,null);
        await Task.Delay(1600);window.Show();Call("Render");await Task.Delay(100);
        check("show after preview deadline cannot replay selection flash",Clock()==null&&!glow.HasAnimatedProperties&&glow.Opacity==0,null);
        Select(false);State("away");Set("awaySince",(DateTimeOffset?)DateTimeOffset.Now.AddSeconds(-125));Select(true);var selectedAt=DateTimeOffset.Now;
        Call("RenderVisualExtras",selectedAt.AddSeconds(29),false);
        check("reset schedule waits a full away threshold without an animation clock",Clock()==null&&!glow.HasAnimatedProperties,null);
        Call("RenderVisualExtras",selectedAt.AddSeconds(30.7),false);await Task.Delay(60);Record("scheduled-flash");
        check("fresh schedule starts regular flashing after the threshold",Clock()!=null&&glow.HasAnimatedProperties&&glow.Opacity>.2,new{glow.Opacity});
        Select(false);check("Off releases an active scheduled reminder",Clock()==null&&!glow.HasAnimatedProperties&&glow.Opacity==0,null);
        State("paused");Select(true);var sameClock=Clock();Select(true);
        check("redundant same Flash selection cannot restart its preview",ReferenceEquals(sameClock,Clock()),null);
        Call("TimeChanged",null,EventArgs.Empty);await Task.Delay(80);Call("Render");
        check("system clock correction cancels selection preview",Clock()==null&&!glow.HasAnimatedProperties&&glow.Opacity==0,null);
        Select(false);State("paused");Select(true);var timing=Stopwatch.StartNew();var curve=new List<(double seconds,double opacity)>();
        while(timing.Elapsed.TotalSeconds<1.75){curve.Add((timing.Elapsed.TotalSeconds,glow.Opacity));await Task.Delay(25);}
        var peak=curve.MaxBy(p=>p.opacity);var error=curve.Where(p=>p.seconds<1.5).Max(p=>Math.Abs(p.opacity-(1-Math.Cos(p.seconds/1.5*Math.PI*2))/2));
        check("actual preview clock follows the Mac 1.5 second cosine pulse",peak.opacity>.95&&peak.seconds is >.60 and <.9&&error<.2,new{peakSeconds=peak.seconds,peakOpacity=peak.opacity,maxSampleError=error,samples=curve.Count});
        check("sampled preview ends at zero and releases all glow animation",curve[^1].opacity==0&&Clock()==null&&!glow.HasAnimatedProperties,null);
        Call("CollapseToTimer");await Task.Delay(450);Call("EnableNativeForeground");Call("EnableNativePulses");await Task.Delay(150);
        Select(false);Select(true);await Task.Delay(80);
        check("selection preview suspends native foreground for live glow",!Field<bool>("nativeForegroundActive")&&Clock()!=null&&glow.HasAnimatedProperties,null);
        await Task.Delay(1650);
        check("preview completion automatically restores native paused foreground",Field<bool>("nativeForegroundActive")&&Clock()==null&&!glow.HasAnimatedProperties,null);
        File.WriteAllText(Path.Combine(output,"preview-curve.json"),JsonSerializer.Serialize(curve.Select(p=>new{p.seconds,p.opacity}),AtomicStore.JsonOptions));
        File.WriteAllText(Path.Combine(output,"reminder-schedule.json"),JsonSerializer.Serialize(new{productDllSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),samples,scope="Own actual WPF selectors and clocks; controlled tracking/away timestamps and onboarding handlers. No native input, physical session, game or wall-clock threshold wait."},AtomicStore.JsonOptions));
    }
}
