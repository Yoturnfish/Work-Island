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
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunReminderScheduleLive(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        void Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var engine=Field<TrackingEngine>("engine");var store=Field<AtomicStore>("store");var settings=engine.Data.Settings;
        settings.Paused=true;settings.Rules=[];settings.ReduceMotion=false;settings.SuperReminderEnabled=false;settings.BackgroundStyle="black";
        engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new(null,null,0));Call("Render");await Task.Delay(400);
        Call("EnableNativeForeground");Call("EnableNativePulses");await Task.Delay(100);
        window.Left=320;window.Top=260;window.Topmost=true;window.Title="Work Island · 提醒预览验收";
        Call("ToggleExpanded",null,new RoutedEventArgs());Call("ShowPage","settings",false);await Task.Delay(300);
        var picker=Field<ComboBox>("ReminderPicker");picker.BringIntoView();window.UpdateLayout();window.Activate();
        var glow=Field<FrameworkElement>("ReminderGlow");var watch=Stopwatch.StartNew();var changes=new List<object>();
        var curve=new List<(double seconds,double opacity,bool clock)>();var enabledAt=(double?)null;var enabled=false;var disabled=false;
        var capturedFrames=new List<object>();var nextFrame=0;
        SelectionChangedEventHandler changed=(_,_)=>
        {
            if(settings.SuperReminderEnabled){enabled=true;enabledAt=watch.Elapsed.TotalSeconds;}else disabled=true;
            changes.Add(new{seconds=watch.Elapsed.TotalSeconds,settings.SuperReminderEnabled,saved=store.Load().Settings.SuperReminderEnabled,engine.Current.State});
        };
        picker.SelectionChanged+=changed;
        var source=(HwndSource)PresentationSource.FromVisual(window);
        File.WriteAllText(Path.Combine(output,"input-ready.json"),JsonSerializer.Serialize(new{processId=Environment.ProcessId,windowHandle=source.Handle.ToInt64(),window.Title,scope="Own isolated paused settings window. Official mouse opens Reminder dropdown; keyboard Down/Return selects Flash, then Up/Return selects Off after full preview. Fixture only observes selection and glow properties after preparation."},AtomicStore.JsonOptions));
        try
        {
            while(watch.Elapsed.TotalSeconds<300&&!File.Exists(Path.Combine(output,"stop-review.txt")))
            {
                if(enabledAt is {} start&&watch.Elapsed.TotalSeconds-start<2.2)
                {
                    var elapsed=watch.Elapsed.TotalSeconds-start;
                    curve.Add((elapsed,glow.Opacity,Field<AnimationClock?>("reminderClock")!=null));
                    if(nextFrame<8&&elapsed>=nextFrame*.2)
                    {
                        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth*2),(int)Math.Ceiling(window.ActualHeight*2),192,192,PixelFormats.Pbgra32);
                        bitmap.Render(Field<FrameworkElement>("OuterBorder"));var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        var filename="actual-selection-frame-"+nextFrame++.ToString("00")+".png";
                        using(var stream=File.Create(Path.Combine(output,filename)))encoder.Save(stream);
                        capturedFrames.Add(new{elapsed,strength=glow.Opacity,filename});
                    }
                }
                await Task.Delay(25);
            }
            check("official dropdown selection enabled and disabled saved reminder",enabled&&disabled&&!settings.SuperReminderEnabled&&!store.Load().Settings.SuperReminderEnabled,changes);
            check("official Flash selection shows visible preview while paused",engine.Current.State=="paused"&&curve.Count>20&&curve.Max(p=>p.opacity)>.8,new{samples=curve.Count,peak=curve.Count>0?curve.Max(p=>p.opacity):0});
            check("real selection preview ends and releases its clock",curve.Any(p=>p.seconds>1.65&&p.opacity==0&&!p.clock),null);
            check("Off leaves settings page and stopped glow intact",ReferenceEquals(Field<FrameworkElement?>("activePage"),Field<FrameworkElement>("SettingsPage"))&&Field<AnimationClock?>("reminderClock")==null&&!glow.HasAnimatedProperties&&glow.Opacity==0,null);
            check("actual selection captures expanding-glow intermediate frames",capturedFrames.Count==8,capturedFrames);
            File.WriteAllText(Path.Combine(output,"reminder-live.json"),JsonSerializer.Serialize(new{productDllSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),processId=Environment.ProcessId,windowHandle=source.Handle.ToInt64(),changes,curve=curve.Select(p=>new{p.seconds,p.opacity,p.clock}),scope="Official Computer Use mouse opens own WPF dropdown and keyboard selects its options. Read-only selection/opacity/clock observers, isolated paused data. No generated input or direct selection/handler calls after preparation. Not physical hardware, game, or full away-threshold wall-clock acceptance."},AtomicStore.JsonOptions));
        }
        finally{picker.SelectionChanged-=changed;}
    }
}

