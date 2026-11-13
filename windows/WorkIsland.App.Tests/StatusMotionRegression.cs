using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;
using Color=System.Drawing.Color;

internal static partial class Program
{
    private static async Task RunStatusMotionChecks(IslandWindow window,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|System.Reflection.BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var saved=JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions);
        var folder=Path.GetDirectoryName(Field<AtomicStore>("store").Path)!;
        var source=Field<FullscreenFixture>("fullscreenSource");
        var motion=new StatusItemMotion();var green=Color.FromArgb(64,255,97);var orange=Color.FromArgb(255,149,0);
        motion.Retarget(green,"tracking",.5,false,0,null);motion.Retarget(orange,"away",.5,false,1,-5);
        var midpoint=motion.ColorAt(1.175);
        check("menu color has real midpoint during Mac .35 second transition",midpoint!=green&&midpoint!=orange&&motion.ColorAt(1.35)==orange,null);
        motion.Retarget(green,"tracking",.5,false,1.175,null);
        check("rapid menu color reversal preserves current presentation",motion.ColorAt(1.175)==midpoint,null);
        motion.Retarget(orange,"away",.5,true,1.2,20);
        check("reduce motion settles menu color and progress immediately",motion.ColorAt(1.2)==orange&&motion.Mode=="static"&&!motion.NeedsFrames(1.2),null);
        motion.Retarget(orange,"away",.5,false,2,0);
        check("menu away pulse retains filled cells and changes only unfinished cells",motion.CellOpacity(0,5,3.4)==.72&&Math.Abs(motion.CellOpacity(8,5,3.4)-.35)<.0001,null);
        check("menu away pulse completes Mac 2.8 second cycle",Math.Abs(motion.CellOpacity(8,5,4.8)-.10)<.0001,null);
        motion.Retarget(orange,"away",1,false,5,3);
        check("completed away goal never starts menu breathing",motion.Mode=="static",null);
        motion.Retarget(green,"tracking",1,false,6,null);
        check("completed tracking keeps scanning with Mac period",motion.Mode=="scan"&&Math.Abs(motion.CellOpacity(2,10,6.9)-motion.CellOpacity(2,10,9.7))<.0001,null);
        try
        {
            engine.Data.Settings.Rules=[new("status-motion.exe","Status motion")];engine.Data.Settings.ReduceMotion=false;
            engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new("status-motion.exe","Status motion",0));Call("Render");
            Call("ChangeDisplayMode","menuBar");
            var native=Field<object>("statusItem");var animation=Field<DispatcherTimer>("statusAnimationTimer");
            int Frames()=>(int)native.GetType().GetProperty("FrameUpdates")!.GetValue(native)!;
            double NativeSeconds(string name)=>(double)native.GetType().GetProperty(name)!.GetValue(native)!;
            check("hidden menu island stops its unused WPF progress clock",!window.IsVisible&&!Field<PixelProgress>("GoalProgress").ScanAnimating,null);
            // Separate startup/JIT/compositor warmup from sustained animation.
            var coldStart=Frames();var warmClock=Stopwatch.StartNew();await Task.Delay(2800);
            var warmup=new{durationSeconds=warmClock.Elapsed.TotalSeconds,frameUpdates=Frames()-coldStart};
            var before=Frames();var renderBefore=Field<long>("renderCalls");var drawBefore=Field<long>("statusDrawCalls");var rasterBefore=Field<double>("statusRasterSeconds");var shellBefore=NativeSeconds("ShellSeconds");var iconBefore=NativeSeconds("IconSeconds");var process=Process.GetCurrentProcess();var threads=process.Threads.Cast<ProcessThread>().ToDictionary(t=>t.Id,t=>t.TotalProcessorTime.TotalSeconds);var cpu=process.TotalProcessorTime;var allocated=TestRuntime.GetTotalAllocatedBytes(true);var clock=Stopwatch.StartNew();
            await Task.Delay(2800);process.Refresh();var duration=clock.Elapsed.TotalSeconds;var cpuSeconds=(process.TotalProcessorTime-cpu).TotalSeconds;
            var samples=new{durationSeconds=duration,frameUpdates=Frames()-before,drawCalls=Field<long>("statusDrawCalls")-drawBefore,bodyRenders=Field<long>("renderCalls")-renderBefore,trackingTimer=Field<DispatcherTimer>("timer").IsEnabled,framesPerSecond=(Frames()-before)/duration,cpuSeconds,rasterSeconds=Field<double>("statusRasterSeconds")-rasterBefore,iconSeconds=NativeSeconds("IconSeconds")-iconBefore,shellSeconds=NativeSeconds("ShellSeconds")-shellBefore,allocatedBytes=TestRuntime.GetTotalAllocatedBytes(true)-allocated,warmup,threads=process.Threads.Cast<ProcessThread>().Where(t=>threads.ContainsKey(t.Id)).Select(t=>new{id=t.Id,cpuSeconds=t.TotalProcessorTime.TotalSeconds-threads[t.Id]}).OrderByDescending(t=>t.cpuSeconds).Take(5).ToArray()};
            File.WriteAllText(Path.Combine(folder,"status-motion-runtime.json"),JsonSerializer.Serialize(samples,AtomicStore.JsonOptions));
            check("native status animation continues while island is hidden",animation.IsEnabled&&Frames()-before>=35,samples);
            check("native animation uses bounded CPU and allocation",cpuSeconds/duration<.2&&samples.allocatedBytes<16000000,samples);
            var captures=new System.Collections.Generic.List<object>();var captureClock=Stopwatch.StartNew();
            var captureFolder=Path.Combine(folder,"submitted-frames");Directory.CreateDirectory(captureFolder);
            Action<Bitmap> submitted=bitmap=>
            {
                var file=$"frame-{captures.Count:00000}.png";bitmap.Save(Path.Combine(captureFolder,file));
                captures.Add(new{file,elapsedSeconds=captureClock.Elapsed.TotalSeconds,state=engine.Current.State});
            };
            var submittedEvent=native.GetType().GetEvent("FrameSubmitted")!;submittedEvent.AddEventHandler(native,submitted);
            try
            {
                engine.Observe(DateTimeOffset.Now,new("unmatched.exe","Unmatched",0));Call("Render");await Task.Delay(100);
                engine.Observe(DateTimeOffset.Now,new("status-motion.exe","Status motion",0));Call("Render");await Task.Delay(500);
                engine.Observe(DateTimeOffset.Now,new("unmatched.exe","Unmatched",0));Call("Render");
                typeof(IslandWindow).GetField("awaySince",Flags)!.SetValue(window,(DateTimeOffset?)DateTimeOffset.Now.AddSeconds(-10));Call("Render");
                await Task.Delay(3000);
            }
            finally{submittedEvent.RemoveEventHandler(native,submitted);}
            File.WriteAllText(Path.Combine(folder,"submitted-motion.json"),JsonSerializer.Serialize(new{passed=captures.Count>30,method="Timestamped exact DPI-sized bitmap buffers submitted to Shell_NotifyIcon; not a screenshot of Explorer composition",nominalFrameRate=StatusItemMotion.FrameRate,cycleSeconds=StatusItemMotion.CycleSeconds,transitionSeconds=StatusItemMotion.TransitionSeconds,frames=captures},AtomicStore.JsonOptions));
            check("native submitted frames cover color reversal and full away cycle",captures.Count>30,new{frames=captures.Count,durationSeconds=captureClock.Elapsed.TotalSeconds});
            engine.Data.Settings.AwayTimerEnabled=true;Call("Render");await Task.Delay(450);
            var tooltipField=native.GetType().GetField("tooltip",Flags)!;
            var firstTooltip=(string)tooltipField.GetValue(native)!;var credited=engine.TodaySeconds(DateTimeOffset.Now);
            await Task.Delay(1300);
            var lastTooltip=(string)tooltipField.GetValue(native)!;
            check("hidden away timer advances tooltip while coffee remains the selected glyph without crediting work",firstTooltip!=lastTooltip&&Field<StatusGlyphArtwork>("statusArtwork") is not null&&engine.Current.State=="away"&&engine.TodaySeconds(DateTimeOffset.Now)==credited,new{firstTooltip,lastTooltip});
            engine.Data.Settings.AwayTimerEnabled=false;
            engine.Observe(DateTimeOffset.Now,new("status-motion.exe","Status motion",0));Call("Render");
            engine.Data.Settings.ReduceMotion=true;Call("Render");var stopped=Frames();await Task.Delay(150);
            check("reduced motion stops native frame submissions",!animation.IsEnabled&&Frames()==stopped,null);
            engine.Data.Settings.ReduceMotion=false;Call("Render");source.Active=true;Call("RefreshFullscreenAvoidance");stopped=Frames();await Task.Delay(150);
            check("fullscreen stops native animation and preserves background tracking",!animation.IsEnabled&&Frames()==stopped&&!engine.Data.Settings.Paused,null);
            source.Active=false;Call("RefreshFullscreenAvoidance");await Task.Delay(1100);Call("RefreshFullscreenAvoidance");
            check("native animation resumes without reopening menu after game",animation.IsEnabled&&!window.IsVisible,null);
            var userBefore=GetStatusGuiResources(process.Handle,1);var gdiBefore=GetStatusGuiResources(process.Handle,0);
            using var artwork=new StatusGlyphArtwork();
            for(var i=0;i<128;i++)
            {
                using var bitmap=artwork.Render("tracking","green",i/127d,32,scanPhase:i/128d);
                native.GetType().GetMethod("Update")!.Invoke(native,[bitmap,"Work Island isolated resource fixture"]);
            }
            var userAfter=GetStatusGuiResources(process.Handle,1);var gdiAfter=GetStatusGuiResources(process.Handle,0);
            check("native frame replacement does not leak USER or GDI handles",userAfter<=userBefore+2&&gdiAfter<=gdiBefore+2,new{userBefore,userAfter,gdiBefore,gdiAfter});
            for(var i=0;i<16;i++)
            {
                var sample=i*.2;
                using var bitmap=artwork.Render("tracking","green",.5,64,green,scanPhase:sample/StatusItemMotion.CycleSeconds);
                bitmap.Save(Path.Combine(folder,$"menu-scan-{i:00}.png"));
            }
            Call("ChangeDisplayMode","floating");check("leaving menu mode releases animation timer",!animation.IsEnabled,null);
        }
        finally
        {
            source.Active=false;typeof(IslandWindow).GetField("fullscreenSuppressed",Flags)!.SetValue(window,false);
            var restored=JsonSerializer.Deserialize<AppState>(saved,AtomicStore.JsonOptions)!;engine.Data.Settings=restored.Settings;
            engine.Data.Sessions.Clear();engine.Data.Sessions.AddRange(restored.Sessions);engine.ResetObservation();Call("ApplyDisplayMode");Call("ProfileChanged");
            Field<DispatcherTimer>("focusReturn").Stop();Field<DispatcherTimer>("statusAnimationTimer").Stop();window.Topmost=false;
        }
    }
    [DllImport("user32.dll",EntryPoint="GetGuiResources")]private static extern uint GetStatusGuiResources(nint process,uint flags);
}
