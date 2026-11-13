using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunNativeDragReview(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;var store=Field<AtomicStore>("store");
        settings.HasCompletedOnboarding=true;settings.DisplayMode="floating";settings.BackgroundStyle="black";settings.BackgroundOpacity=1;settings.ReduceMotion=false;settings.Paused=false;settings.AwayTimerEnabled=false;settings.SuperReminderEnabled=false;
        settings.Rules=[new("drag-review.exe","Drag fixture")];settings.Websites=[];
        var now=DateTimeOffset.Now;engine.Data.Sessions.Clear();engine.Data.Sessions.Add(new(){ApplicationIdentifier="drag-review.exe",ApplicationName="Drag fixture",DayKey=LogicalDay.Key(now,engine.Zone,settings.DayResetMinutes),StartedAt=now.AddHours(-2),EndedAt=now,EffectiveSeconds=7200});
        Field<DispatcherTimer>("fullscreenTimer").Stop();window.Left=600;window.Top=400;window.Topmost=true;window.ShowInTaskbar=true;window.Title="Work Island · 原生拖动验收";
        var bg=new Window{Title="Work Island · 拖动自有背景",Left=450,Top=270,Width=750,Height=400,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowActivated=false,ShowInTaskbar=false,Background=new SolidColorBrush(Color.FromRgb(217,227,235))};bg.Show();
        typeof(IslandWindow).GetField("fidelityBackground",Flags)!.SetValue(window,bg);
        Call("ApplyMaterial");Call("NotifyPointer",false);engine.ResetObservation();engine.Observe(now,new("drag-review.exe","Drag fixture",0));Call("Render");await Task.Delay(450);window.EnableNativeForeground();window.EnableNativePulses();await Task.Delay(150);
        settings.WindowX=window.Left;settings.WindowY=window.Top;Call("Persist");
        var originalX=window.Left;var originalY=window.Top;var originalCaptures=Field<int>("nativeForegroundCaptures");var locations=new List<object>();var downs=0;
        var watch=Stopwatch.StartNew();
        object Snapshot()=>new{at=DateTimeOffset.Now,elapsedMs=watch.Elapsed.TotalMilliseconds,left=window.Left,top=window.Top,native=Field<bool>("nativeForegroundActive"),captures=Field<int>("nativeForegroundCaptures"),savedX=store.Load().Settings.WindowX,savedY=store.Load().Settings.WindowY};
        EventHandler moved=(_,_)=>locations.Add(Snapshot());MouseButtonEventHandler down=(_,_)=>downs++;
        window.LocationChanged+=moved;window.PreviewMouseDown+=down;
        IslandWindow? restored=null;
        try
        {
            Call("SaveScreenRegion",Path.Combine(output,"before-drag-screen.png"),23);
            File.WriteAllText(Path.Combine(output,"input-ready.json"),JsonSerializer.Serialize(new{processId=Environment.ProcessId,windowHandle=(long)new WindowInteropHelper(window).Handle,window.Title,originalX,originalY,originalCaptures,scope="Independent actual WPF window, current App/Core and isolated state. Waiting only for official drag. No programmatic position changes or mouse handlers after ready; observer writes read-only snapshots."},AtomicStore.JsonOptions));
            while(!File.Exists(Path.Combine(output,"finish-drag.txt"))&&watch.Elapsed.TotalSeconds<600)
            {File.WriteAllText(Path.Combine(output,"drag-progress.json"),JsonSerializer.Serialize(new{downs,locations=locations.ToArray(),current=Snapshot()},AtomicStore.JsonOptions));await Task.Delay(100);}
            var last=store.Load();var movedFar=Math.Abs(window.Left-originalX)>5||Math.Abs(window.Top-originalY)>5;
            check("official mouse drag reaches owned compact window",downs>0&&File.Exists(Path.Combine(output,"finish-drag.txt")),new{downs});
            check("official drag actually changes live window position",movedFar&&locations.Count>0,new{window.Left,window.Top,originalX,originalY,locations=locations.Count});
            check("drag release durably stores the actual new position",last.Settings.WindowX==window.Left&&last.Settings.WindowY==window.Top,new{last.Settings.WindowX,last.Settings.WindowY,window.Left,window.Top});
            check("plain drag preserves native foreground without a new texture",Field<bool>("nativeForegroundActive")&&Field<int>("nativeForegroundCaptures")==originalCaptures,new{native=Field<bool>("nativeForegroundActive"),captures=Field<int>("nativeForegroundCaptures"),originalCaptures});
            check("drag preserves compact layout and original data",!Field<bool>("isExpanded")&&last.Sessions.Count==1&&last.Sessions[0].EffectiveSeconds==7200&&last.Settings.Rules.Count==1,null);
            Call("SaveScreenRegion",Path.Combine(output,"after-drag-screen.png"),23);
            File.WriteAllText(Path.Combine(output,"drag-positions.json"),JsonSerializer.Serialize(new{originalX,originalY,finalX=window.Left,finalY=window.Top,originalCaptures,downs,locations},AtomicStore.JsonOptions));
            window.LocationChanged-=moved;window.PreviewMouseDown-=down;
            // Reconstruct a fresh product window from disk. The original is
            // hidden and marked exiting; no timer or second writer is active.
            typeof(IslandWindow).GetField("exiting",Flags)!.SetValue(window,true);window.Hide();
            restored=new IslandWindow(store,last,false,null,startupRegistration:new IsolatedStartupRegistration(Path.Combine(output,"restore-startup.json"),"isolated-position-restore"),recognitionSource:new RecognitionFixture(),applicationPicker:new ApplicationPickerFixture(),fullscreenSource:new FullscreenFixture());
            restored.Title="Work Island · 拖动位置恢复验收";restored.ShowInTaskbar=true;restored.ShowActivated=false;
            restored.Loaded+=(_,_)=>((DispatcherTimer)typeof(IslandWindow).GetField("timer",Flags)!.GetValue(restored)!).Stop();restored.Show();await Task.Delay(150);
            ((DispatcherTimer)typeof(IslandWindow).GetField("fullscreenTimer",Flags)!.GetValue(restored)!).Stop();restored.EnableNativeForeground();restored.EnableNativePulses();await Task.Delay(500);
            check("new product window loads saved dragged coordinates without test repositioning",Math.Abs(restored.Left-last.Settings.WindowX!.Value)<.01&&Math.Abs(restored.Top-last.Settings.WindowY!.Value)<.01,new{restored.Left,restored.Top,last.Settings.WindowX,last.Settings.WindowY});
            File.WriteAllText(Path.Combine(output,"restore-ready.json"),JsonSerializer.Serialize(new{processId=Environment.ProcessId,windowHandle=(long)new WindowInteropHelper(restored).Handle,restored.Title,restored.Left,restored.Top,scope="New IslandWindow constructor restored prior saved coordinates in same test process. Original hidden/exiting, no programmatic restored Left/Top writes. Not a process restart or session test; await official observation only."},AtomicStore.JsonOptions));
            var restoreWatch=Stopwatch.StartNew();while(!File.Exists(Path.Combine(output,"finish-restore.txt"))&&restoreWatch.Elapsed.TotalSeconds<600)await Task.Delay(100);
            check("official tool observes restored window before completion",File.Exists(Path.Combine(output,"finish-restore.txt")),null);
        }
        finally
        {
            window.LocationChanged-=moved;window.PreviewMouseDown-=down;
            if(restored is not null){typeof(IslandWindow).GetField("exiting",Flags)!.SetValue(restored,true);restored.Close();}
            typeof(IslandWindow).GetField("fidelityBackground",Flags)!.SetValue(window,null);bg.Close();
        }
    }
}
