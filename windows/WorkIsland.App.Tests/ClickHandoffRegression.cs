using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunClickHandoff(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        void Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|System.Reflection.BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;
        Field<DispatcherTimer>("fullscreenTimer").Stop();Field<DispatcherTimer>("focusReturn").Stop();
        settings.HasCompletedOnboarding=true;settings.Paused=false;settings.ReduceMotion=false;settings.BackgroundStyle="glass";settings.BackgroundOpacity=1;settings.Rules=[new("handoff.exe","Handoff")];
        window.Left=700;window.Top=300;window.Topmost=true;
        Call("ApplyMaterial");Call("NotifyPointer",false);engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new("handoff.exe","Handoff",0));Call("Render");await Task.Delay(550);
        var guard=new Window{Title="Work Island isolated click capture",WindowStyle=WindowStyle.None,ShowInTaskbar=false,ShowActivated=false,Left=600,Top=200,Width=650,Height=300,Background=Brushes.White};
        guard.Show();typeof(IslandWindow).GetField("fidelityBackground",Flags)!.SetValue(window,guard);Call("PositionTestBackground");
        try
        {
            Call("EnableNativeForeground");Call("EnableNativePulses");await Task.Delay(150);
            check("click capture begins in actual native compact rendering",Field<bool>("nativeForegroundActive"),null);
            var center=Field<FrameworkElement>("StatusIcon").PointToScreen(new Point(12,12));
            var samples=new List<object>();var counts=new List<int>();var clock=Stopwatch.StartNew();var stop=0;
            var sampler=Task.Run(()=>
            {
                using var bitmap=new System.Drawing.Bitmap(24,24);using var graphics=System.Drawing.Graphics.FromImage(bitmap);
                while(Volatile.Read(ref stop)==0)
                {
                    graphics.CopyFromScreen((int)center.X-12,(int)center.Y-12,0,0,bitmap.Size);
                    var ink=0;for(var y=0;y<24;y++)for(var x=0;x<24;x++){var c=bitmap.GetPixel(x,y);if(Math.Max(c.R,Math.Max(c.G,c.B))-Math.Min(c.R,Math.Min(c.G,c.B))>65)ink++;}
                    var at=clock.Elapsed.TotalMilliseconds;counts.Add(ink);samples.Add(new{atMs=at,coloredPixels=ink});
                    bitmap.Save(Path.Combine(output,$"click-frame-{counts.Count:0000}.png"));Thread.Sleep(4);
                }
            });
            await Task.Delay(90);
            var downAt=clock.Elapsed.TotalMilliseconds;
            window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent});
            var captures=Field<int>("nativeForegroundCaptures");
            check("ordinary background press keeps the current native picture",Field<bool>("nativeForegroundActive"),null);
            // Deliberate UI-thread workload exposes an asynchronous renderer gap.
            // This is an isolated stress fixture, not a real hardware mouse click.
            Thread.Sleep(65);await Task.Delay(150);
            var upAt=clock.Elapsed.TotalMilliseconds;
            window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseUpEvent});
            await Task.Delay(100);Volatile.Write(ref stop,1);await sampler;
            check("screen pixels retain the colored status icon across press and release",counts.Count>12&&counts.Min()>30,new{frames=counts.Count,minimum=counts.Min(),maximum=counts.Max(),downAt,upAt});
            check("ordinary background click does not upload a replacement texture",Field<int>("nativeForegroundCaptures")==captures,new{before=captures,after=Field<int>("nativeForegroundCaptures")});
            check("mouse release returns to native rendering",Field<bool>("nativeForegroundActive"),null);
            Field<FrameworkElement>("TitleLabel").RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent});
            check("ordinary title press also preserves native rendering",Field<bool>("nativeForegroundActive"),null);
            window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseUpEvent});await Task.Delay(60);
            Field<FrameworkElement>("ExpandButton").RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent});
            check("interactive icon press still enables live pressed feedback",!Field<bool>("nativeForegroundActive"),null);
            window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseUpEvent});await Task.Delay(80);
            Call("ToggleExpanded",null,new RoutedEventArgs());await Task.Delay(65);Call("CollapseToTimer");await Task.Delay(350);
            check("rapid expand collapse settles in native compact rendering",Field<bool>("nativeForegroundActive")&&!Field<bool>("isExpanded"),null);
            Call("SuspendNativeForeground");window.Hide();
            check("hide suspends native foreground immediately",!Field<bool>("nativeForegroundActive"),null);
            window.Show();await Task.Delay(550);
            check("show recovers the native foreground after cancellation",Field<bool>("nativeForegroundActive"),new{failure=Field<string?>("nativeForegroundFailure")});
            File.WriteAllText(Path.Combine(output,"click-pixels.json"),JsonSerializer.Serialize(new{downAt,upAt,frames=samples,scope="Own isolated HWND, white owned background, independent 24x24 screen-pixel sampler and routed PreviewMouseDown/Up. Deliberate 65ms UI-thread workload. Not hardware mouse or complete navigation video."},AtomicStore.JsonOptions));
        }
        finally{typeof(IslandWindow).GetField("fidelityBackground",Flags)!.SetValue(window,null);guard.Close();}
    }
}
