using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunNativePulseController(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var progress=Field<PixelProgress>("GoalProgress");var border=Field<Border>("OuterBorder");var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;var scale=(ScaleTransform)Field<Grid>("StatusIcon").RenderTransform;
        T Pixel<T>(string name)=>(T)typeof(PixelProgress).GetField(name,Flags)!.GetValue(progress)!;
        object? Surface(string name)=>typeof(IslandWindow).GetField(name,Flags)!.GetValue(window);
        int Count(object? surface,string name)=>surface==null?0:(int)surface.GetType().GetProperty(name,Flags)!.GetValue(surface)!;
        bool Active()=>Field<bool>("nativeForegroundActive");long Epoch()=>Field<long>("statusPulseEpoch");
        object Snapshot()=>new{state=engine.Current.State,active=Active(),failure=Field<string?>("nativeForegroundFailure"),statusEpoch=Epoch(),statusNative=Field<bool>("statusPulseNative"),scale=scale.ScaleX,wpfStatus=scale.HasAnimatedProperties,wpfCells=Pixel<System.Windows.Shapes.Path>("unfilled").HasAnimatedProperties,progress.ScanAnimating,progress.PulseAnimating,physicalOpacity=window.Opacity,border.Opacity,glyphPulses=Count(Surface("nativeGlyphSurface"),"ActivePulses"),cellsPulses=Count(Surface("nativeCellsSurface"),"ActivePulses")};
        void State(string state,bool breathe=false)
        {
            settings.Paused=state=="paused";engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new(state=="tracking"?"pulse-controller.exe":"other.exe","Controller fixture",0));Call("Render");
            if(state=="away"&&breathe){typeof(IslandWindow).GetField("awaySince",Flags)!.SetValue(window,(DateTimeOffset?)DateTimeOffset.Now.AddSeconds(-11));Call("Render");}
        }
        void SendKey(Key key,bool down)=>window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),Environment.TickCount,key){RoutedEvent=down?Keyboard.PreviewKeyDownEvent:Keyboard.PreviewKeyUpEvent});
        settings.HasCompletedOnboarding=true;settings.Rules=[new("pulse-controller.exe","Controller fixture")];settings.BackgroundStyle="black";settings.BackgroundOpacity=.5;settings.ReduceMotion=false;settings.GoalSeconds=14400;settings.AwayTimerEnabled=false;settings.SuperReminderEnabled=false;
        var now=DateTimeOffset.Now;engine.Data.Sessions.Clear();engine.Data.Sessions.Add(new(){ApplicationIdentifier="pulse-controller.exe",ApplicationName="Controller fixture",DayKey=LogicalDay.Key(now,engine.Zone,settings.DayResetMinutes),StartedAt=now.AddHours(-2),EndedAt=now,EffectiveSeconds=7200});
        Field<DispatcherTimer>("fullscreenTimer").Stop();window.Left=320;window.Top=260;window.Topmost=true;Call("ApplyMaterial");Call("NotifyPointer",false);State("paused");await Task.Delay(500);Call("EnableNativeForeground");Call("EnableNativePulses");await Task.Delay(100);
        check("paused cold start creates foreground without pretending to scan",Active()&&!progress.ScanAnimating&&Count(Surface("nativeGlyphSurface"),"ActivePulses")==2,Snapshot());
        if(!Active())return;
        check("paused stable native state removes the owned WPF loop",!scale.HasAnimatedProperties&&Field<object?>("statusPulseClock")==null&&window.Opacity==1&&border.Opacity==0,Snapshot());
        check("glyph reuses the foreground graphics device",Count(Surface("nativeForegroundSurface"),"DeviceCreates")==1&&Count(Surface("nativeGlyphSurface"),"DeviceCreates")==0,null);
        State("away");await Task.Delay(70);check("paused to away presents animated WPF state on the same native plane",Active()&&Field<FrameworkElement>("CoffeeIcon").Opacity>0&&Field<FrameworkElement>("CoffeeIcon").Opacity<1,Snapshot());await Task.Delay(450);
        check("away before delay has native glyph and static unfilled cells",Active()&&Count(Surface("nativeGlyphSurface"),"ActivePulses")==2&&Count(Surface("nativeCellsSurface"),"ActivePulses")==0&&!progress.PulseAnimating,Snapshot());
        var epoch=Epoch();var glyphStarts=Count(Surface("nativeGlyphSurface"),"ScaleStarts");State("away",true);await Task.Delay(80);
        check("ten second unfinished-away threshold adds independent cells pulse",Active()&&Count(Surface("nativeCellsSurface"),"ActivePulses")==1&&progress.PulseAnimating&&!Pixel<System.Windows.Shapes.Path>("unfilled").HasAnimatedProperties,Snapshot());
        check("enabling cells does not restart glyph phase",Epoch()==epoch&&Count(Surface("nativeGlyphSurface"),"ScaleStarts")==glyphStarts,null);
        check("three layers share one graphics device",Count(Surface("nativeForegroundSurface"),"DeviceCreates")==1&&Count(Surface("nativeGlyphSurface"),"DeviceCreates")==0&&Count(Surface("nativeCellsSurface"),"DeviceCreates")==0,null);
        var captures=Field<int>("nativeForegroundCaptures");var cellStarts=Count(Surface("nativeCellsSurface"),"OpacityStarts");var references=Count(Surface("nativeGlyphSurface"),"AnimationReferences");var glyphUploads=Count(Surface("nativeGlyphSurface"),"Uploads");var cellsUploads=Count(Surface("nativeCellsSurface"),"Uploads");
        engine.Data.Sessions[0].EffectiveSeconds+=60;Call("Render");await Task.Delay(80);
        check("visible time refresh uploads without restarting either native pulse",Field<int>("nativeForegroundCaptures")==captures+1&&Count(Surface("nativeGlyphSurface"),"ScaleStarts")==glyphStarts&&Count(Surface("nativeCellsSurface"),"OpacityStarts")==cellStarts&&Count(Surface("nativeGlyphSurface"),"AnimationReferences")==references,Snapshot());
        check("time-only refresh reuses both pulse textures",Count(Surface("nativeGlyphSurface"),"Uploads")==glyphUploads&&Count(Surface("nativeCellsSurface"),"Uploads")==cellsUploads,null);
        var handoffs=new List<object>();
        for(var index=0;index<16;index++)
        {
            Call("SuspendNativeForeground");await Task.Delay(25);var phase=(Stopwatch.GetElapsedTime(epoch).TotalSeconds/5.6)%1;var expected=1+.08*(.5-.5*Math.Cos(phase*2*Math.PI));var error=Math.Abs(scale.ScaleX-expected);
            handoffs.Add(new{index,phase,scale=scale.ScaleX,expected,error,wpf=scale.HasAnimatedProperties});Call("RefreshNativeForeground");await Task.Delay(25);
            check("handoff "+index+" retains phase and removes fallback clocks",error<.006&&Epoch()==epoch&&Active()&&!scale.HasAnimatedProperties&&Field<object?>("statusPulseClock")==null,new{error,epoch=Epoch(),snapshot=Snapshot()});
        }
        var glyph=Surface("nativeGlyphSurface");var cells=Surface("nativeCellsSurface");
        check("repeated handoffs detach all prior native controllers",Count(glyph,"ActivePulses")==2&&Count(cells,"ActivePulses")==1&&Count(glyph,"DetachedControllers")>=32&&Count(cells,"DetachedControllers")>=16,new{glyphDetached=Count(glyph,"DetachedControllers"),cellsDetached=Count(cells,"DetachedControllers")});
        Call("ToggleExpanded",null,new RoutedEventArgs());await Task.Delay(65);check("expanding a pulse state keeps the visible fade on its native plane",Active()&&Field<FrameworkElement>("CompactPanel").Opacity>0&&Field<FrameworkElement>("CompactPanel").Opacity<1,Snapshot());await Task.Delay(300);
        check("hidden compact page has no pulse clocks or native animations",!scale.HasAnimatedProperties&&!progress.PulseAnimating&&Count(glyph,"ActivePulses")==0&&Count(cells,"ActivePulses")==0,Snapshot());
        Call("CollapseToTimer");await Task.Delay(350);check("returning to compact resumes native pulse without a timer tick",Active()&&!scale.HasAnimatedProperties,Snapshot());
        settings.ReduceMotion=true;Call("Render");await Task.Delay(50);check("reduce motion stops WPF and native pulse paths",!Active()&&!scale.HasAnimatedProperties&&!progress.PulseAnimating&&Count(glyph,"ActivePulses")==0&&Count(cells,"ActivePulses")==0,Snapshot());
        settings.ReduceMotion=false;Call("Render");await Task.Delay(450);check("disabling reduce motion restores pulse rendering",Active(),Snapshot());
        SendKey(Key.LeftCtrl,true);SendKey(Key.LeftShift,true);Call("Render");await Task.Delay(50);check("held keys keep away animation on live WPF",!Active()&&scale.HasAnimatedProperties,Snapshot());SendKey(Key.LeftCtrl,false);await Task.Delay(30);check("partial key release does not snapshot a held interaction",!Active(),null);SendKey(Key.LeftShift,false);await Task.Delay(60);check("last key release restores native pulse automatically",Active()&&!scale.HasAnimatedProperties,Snapshot());
        window.Hide();await Task.Delay(50);check("hiding removes every pulse path",!Active()&&!scale.HasAnimatedProperties&&!progress.PulseAnimating&&Count(glyph,"ActivePulses")==0&&Count(cells,"ActivePulses")==0,Snapshot());window.Show();Call("Render");await Task.Delay(450);check("showing restores native pulse without focus activation",Active()&&!window.IsActive,Snapshot());
        State("paused");await Task.Delay(60);State("away",true);await Task.Delay(60);State("paused");await Task.Delay(60);State("away",true);await Task.Delay(450);
        check("rapid state reversals settle on the latest glyph and native pulse",Active()&&engine.Current.State=="away"&&!Field<bool>("statusPulseHandoff")&&!scale.HasAnimatedProperties&&Count(glyph,"ActivePulses")==2&&Count(cells,"ActivePulses")==1,Snapshot());
        var latestEpoch=Epoch();var latestCaptures=Field<int>("nativeForegroundCaptures");for(var index=0;index<4;index++){Call("Render");await Task.Delay(20);}
        check("settled reversals retain their epoch without redundant texture uploads",Epoch()==latestEpoch&&Field<int>("nativeForegroundCaptures")==latestCaptures,null);
        settings.AwayTimerEnabled=true;Call("Render");await Task.Delay(70);var awayText=Field<TextBlock>("TimeLabel").Text;var awayCaptures=Field<int>("nativeForegroundCaptures");var awayEpoch=Epoch();glyphUploads=Count(glyph,"Uploads");cellsUploads=Count(cells,"Uploads");
        await Task.Delay(1100);Call("Render");await Task.Delay(70);
        check("actual away-clock seconds refresh without restarting or reuploading pulse planes",Active()&&Field<TextBlock>("TimeLabel").Text!=awayText&&Field<int>("nativeForegroundCaptures")==awayCaptures+1&&Epoch()==awayEpoch&&Count(glyph,"Uploads")==glyphUploads&&Count(cells,"Uploads")==cellsUploads,new{before=awayText,after=Field<TextBlock>("TimeLabel").Text});
        settings.AwayTimerEnabled=false;settings.SuperReminderEnabled=true;typeof(IslandWindow).GetField("awaySince",Flags)!.SetValue(window,(DateTimeOffset?)DateTimeOffset.Now.AddSeconds(-settings.AwayThreshold-.6));Call("Render");await Task.Delay(50);
        var reminder=Field<FrameworkElement>("ReminderGlow");
        check("super reminder restores live WPF and keeps its visible flash",!Active()&&Field<object?>("reminderClock")!=null&&reminder.HasAnimatedProperties&&reminder.Opacity>.01&&Count(glyph,"ActivePulses")==0&&Count(cells,"ActivePulses")==0,new{reminder.Opacity,snapshot=Snapshot()});
        settings.SuperReminderEnabled=false;Call("Render");await Task.Delay(80);var audit=JsonSerializer.SerializeToElement(AuditControllerClocks(window),AtomicStore.JsonOptions);var activeClocks=0;foreach(var clock in audit.GetProperty("clocks").EnumerateArray())if(clock.GetProperty("state").GetString()=="Active")activeClocks++;
        check("disabling reminder removes its clock and restores native pulse",Active()&&Field<object?>("reminderClock")==null&&!reminder.HasAnimatedProperties&&activeClocks==0,new{activeClocks,snapshot=Snapshot()});
        settings.SuperReminderEnabled=true;Call("Render");await Task.Delay(40);window.Hide();await Task.Delay(40);
        check("hiding stops the owned reminder clock",Field<object?>("reminderClock")==null&&!reminder.HasAnimatedProperties,null);
        window.Show();Call("Render");await Task.Delay(40);check("showing retains enabled reminder on the live renderer",!Active()&&Field<object?>("reminderClock")!=null&&reminder.HasAnimatedProperties,null);
        settings.SuperReminderEnabled=false;Call("Render");check("reminder cleared during show still waits for the live state fade",!Active()&&Field<object?>("reminderClock")==null,Snapshot());
        await Task.Delay(400);check("reminder visibility cycle returns to native pulse after fade",Active()&&Field<object?>("reminderClock")==null,Snapshot());
        var background=new Window{Title="Work Island pulse controller evidence background",ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Left=250,Top=190,Width=600,Height=300,Background=Brushes.White};background.Show();typeof(IslandWindow).GetField("fidelityBackground",Flags)!.SetValue(window,background);
        var save=typeof(IslandWindow).GetMethod("SaveScreenRegion",Flags)!;var motion=Path.Combine(output,"motion");var frames=Path.Combine(motion,"frames");Directory.CreateDirectory(frames);var samples=new List<object>();var events=new List<object>();
        try
        {
            State("away",true);await Task.Delay(450);
            var schedule=new (double At,string Name,Action Action)[]{(1.8,"paused",()=>State("paused")),(3.1,"tracking",()=>State("tracking")),(4.5,"away",()=>State("away",true)),(5.7,"expand",()=>Call("ToggleExpanded",null,new RoutedEventArgs())),(6.2,"compact",()=>Call("CollapseToTimer")),(6.9,"reduced",()=>{settings.ReduceMotion=true;Call("Render");}),(7.5,"motion",()=>{settings.ReduceMotion=false;Call("Render");}),(8.3,"hide",()=>window.Hide()),(8.7,"show",()=>{window.Show();Call("Render");}),(9.3,"key down",()=>SendKey(Key.LeftCtrl,true)),(9.7,"key up",()=>SendKey(Key.LeftCtrl,false))};
            var watch=Stopwatch.StartNew();var next=0;
            while(watch.Elapsed.TotalSeconds<11.2)
            {
                while(next<schedule.Length&&watch.Elapsed.TotalSeconds>=schedule[next].At){var item=schedule[next++];item.Action();events.Add(new{at=watch.Elapsed.TotalSeconds,name=item.Name,snapshot=Snapshot()});}
                var at=watch.Elapsed.TotalSeconds;var file=$"frame-{samples.Count:00000}.png";save.Invoke(window,[Path.Combine(frames,file),23]);samples.Add(new{file,elapsedSeconds=at,snapshot=Snapshot()});await Task.Delay(25);
            }
            File.WriteAllText(Path.Combine(motion,"motion-capture.json"),JsonSerializer.Serialize(new{durationSeconds=watch.Elapsed.TotalSeconds,frameCount=samples.Count,frames=samples,events,scope="Actual isolated HWND desktop frames and real production state/navigation/material handlers. Synthetic observations and routed key fixture; no actual mouse/key input, game, hardware or ordinary startup claim."},AtomicStore.JsonOptions));
        }
        finally{typeof(IslandWindow).GetField("fidelityBackground",Flags)!.SetValue(window,null);background.Close();}
        State("away",true);await Task.Delay(450);glyph=Surface("nativeGlyphSurface");cells=Surface("nativeCellsSurface");var parent=Surface("nativeForegroundSurface");
        var presentedBeforeFailure=((InstanceSnapshot)typeof(IslandWindow).GetMethod("CaptureInstanceSnapshot",Flags)!.Invoke(window,null)!).Foreground!.PresentedOpacity;
        ((IDisposable)glyph!).Dispose();engine.Data.Sessions[0].EffectiveSeconds+=60;Call("Render");await Task.Delay(80);
        check("disposed native plane restores live WPF and releases every plane",Field<bool>("nativeForegroundFailed")&&!Active()&&border.Opacity==1&&Math.Abs(window.Opacity-presentedBeforeFailure)<.000001&&scale.HasAnimatedProperties&&Count(glyph,"OwnedReferences")==0&&Count(cells,"OwnedReferences")==0&&Count(parent,"OwnedReferences")==0,new{presentedBeforeFailure,actual=Snapshot()});
        File.WriteAllText(Path.Combine(output,"pulse-controller.json"),JsonSerializer.Serialize(new{productDllSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),handoffs,final=Snapshot(),scope="Actual production pulse controller explicitly opted in on isolated direct-window fixture; ordinary startup has not enabled it. Synthetic observations, routed keys and deliberate own-plane disposal exercise behavior. No real GPU reset or game/hardware acceptance."},AtomicStore.JsonOptions));
    }
}
