using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Security.Cryptography;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunNativePhaseHandoff(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        var progress=Field<PixelProgress>("GoalProgress");var brush=Field<SolidColorBrush>("statusBrush");
        var engine=Field<TrackingEngine>("engine");
        window.Left=320;window.Top=260;
        engine.Data.Settings.Rules=[new("phase.exe","Phase")];engine.Data.Settings.ReduceMotion=false;
        engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new("phase.exe","Phase",0));
        typeof(IslandWindow).GetMethod("Render",Flags|BindingFlags.DeclaredOnly)!.Invoke(window,null);await Task.Delay(450);
        var scan=(TranslateTransform)typeof(PixelProgress).GetField("scan",Flags)!.GetValue(progress)!;
        var band=(System.Windows.Shapes.Rectangle)typeof(PixelProgress).GetField("filledBand",Flags)!.GetValue(progress)!;
        double WpfPhase()=>Math.Clamp((scan.X+band.Width)/(progress.ActualWidth+band.Width),0,1)%1;
        var beforeAttach=WpfPhase();
        typeof(PixelProgress).GetMethod("EnableNativeScan",Flags)!.Invoke(progress,[window,Field<FrameworkElement>("PageHost")]);
        progress.Update(.5,"tracking","matrix",brush,false,30);
        var native=typeof(PixelProgress).GetField("nativeScan",Flags)!.GetValue(progress)??throw new InvalidOperationException("Native scan unavailable");
        object? Native(string method,params object?[] args)=>native.GetType().GetMethod(method,Flags)!.Invoke(native,args);
        double Phase()=>(double)native.GetType().GetProperty("EstimatedPhase",Flags)!.GetValue(native)!;
        double ControllerProgress()=>(double)Native("ReadControllerProgressForReview")!;
        int References()=>((ICollection)native.GetType().GetField("references",Flags)!.GetValue(native)!).Count+((ICollection)native.GetType().GetField("cellReferences",Flags)!.GetValue(native)!).Count;
        static double Distance(double a,double b){var delta=Math.Abs(a-b);return Math.Min(delta,1-delta);}
        check("first native attachment continues existing WPF phase",Distance(beforeAttach,Phase())<.03,new{beforeAttach,afterAttach=Phase()});
        var observations=new List<object>();
        Native("FreezePhaseForReview",.37f);await Task.Delay(70);var frozen1=ControllerProgress();await Task.Delay(150);var frozen2=ControllerProgress();
        check("native controller reads actual frozen phase",Math.Abs(frozen1-.37)<.002&&Math.Abs(frozen2-frozen1)<.002,new{frozen1,frozen2});
        var watch=Stopwatch.StartNew();Native("StartAtPhase",.37d);await Task.Delay(350);var advanced=Phase();watch.Stop();var expected=(.37+watch.Elapsed.TotalSeconds/2.8)%1;
        check("native handoff estimate advances from requested phase",Distance(advanced,expected)<.025,new{advanced,expected,seconds=watch.Elapsed.TotalSeconds,submittedControllerProgress=ControllerProgress(),scope="Monotonic estimate, not compositor position measurement"});
        var initialReferences=References();
        for(var i=0;i<24;i++){Native("StartAtPhase",.4d+i*.001);_ = Phase();}
        check("repeated native phase handoffs release temporary controllers",References()==initialReferences,new{initialReferences,after=References()});
        Native("FreezePhaseForReview",.61f);await Task.Delay(50);var beforeResize=Phase();
        progress.Update(.5,"tracking","waveform",brush,false,30);var afterResize=Phase();
        check("native geometry rebuild preserves scan progress",Distance(beforeResize,afterResize)<.025,new{beforeResize,afterResize});
        observations.Add(new{frozen1,frozen2,advanced,expected,beforeResize,afterResize});
        void Suspend(bool value)=>typeof(PixelProgress).GetMethod("SetNativeScanSuspended",Flags)!.Invoke(progress,[value]);
        // Native-only seek probes above deliberately bypassed the owner clock.
        // Restart through the actual owner before exercising its handoffs.
        progress.Update(.5,"paused","waveform",brush,false,30);progress.Update(.5,"tracking","waveform",brush,false,30);await Task.Delay(1100);
        var previous=Phase();Suspend(true);var immediate=WpfPhase();await Task.Delay(25);var wpf=WpfPhase();
        check("WPF has the handoff position before its first clock tick",Distance(previous,immediate)<.015,new{previous,immediate});
        check("native suspension stops native overlay and continues WPF from previous phase",!(bool)native.GetType().GetProperty("Active",Flags)!.GetValue(native)!&&scan.HasAnimatedProperties&&Distance(wpf,previous+.025/2.8)<.03,new{previous,wpf});
        var beforeResume=WpfPhase();Suspend(false);var resumed=Phase();
        check("native resumption starts at actual WPF animated position",!scan.HasAnimatedProperties&&Distance(beforeResume,resumed)<.015,new{beforeResume,resumed});
        var switches=new List<object>();
        var sequencePhase=Phase();var sequenceWatch=Stopwatch.StartNew();
        for(var i=0;i<12;i++)
        {
            await Task.Delay(35);var n=Phase();Suspend(true);await Task.Delay(25);var w=WpfPhase();
            var toNative=WpfPhase();Suspend(false);var after=Phase();
            var error=Math.Max(Distance(n,w),Distance(toNative,after));
            check($"rapid renderer reversal {i} retains phase",error<.04,new{n,w,toNative,after,error});switches.Add(new{n,w,toNative,after,error});
        }
        var sequenceExpected=(sequencePhase+sequenceWatch.Elapsed.TotalSeconds/2.8)%1;var sequenceActual=Phase();
        check("rapid handoffs retain one continuous phase epoch",Distance(sequenceActual,sequenceExpected)<.015,new{sequenceExpected,sequenceActual,seconds=sequenceWatch.Elapsed.TotalSeconds});
        progress.Update(.5,"tracking","waveform",brush,true,30);
        check("reduced motion stops both renderer clocks after handoffs",!scan.HasAnimatedProperties&&!(bool)native.GetType().GetProperty("Active",Flags)!.GetValue(native)!,null);
        // Screen capture is separate from the property assertions. Opacity 1 is
        // intentional: the scan-only overlay has known grouped-alpha limits.
        var background=new Window{Title="Work Island phase capture guard",ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Left=window.Left-100,Top=window.Top-100,Width=window.Width+200,Height=window.Height+200,Background=Brushes.White};
        var backgroundField=typeof(IslandWindow).GetField("fidelityBackground",Flags)!;
        background.Show();backgroundField.SetValue(window,background);
        typeof(IslandWindow).GetMethod("PositionTestBackground",Flags)!.Invoke(window,null);window.Opacity=1;
        try
        {
            progress.Update(.5,"paused","matrix",brush,false,30);progress.Update(.5,"tracking","matrix",brush,false,30);await Task.Delay(100);
            var motion=Path.Combine(output,"motion");var framesPath=Path.Combine(motion,"frames");Directory.CreateDirectory(framesPath);
            var save=typeof(IslandWindow).GetMethod("SaveScreenRegion",Flags)!;var clock=Stopwatch.StartNew();var samples=new List<object>();var events=new List<object>();
            double[] schedule=[.8,1.2,2,2.08,2.16,2.24,3,3.5,4,4.08,4.16,4.24,5,5.5,6,6.08,6.16,6.24,7,7.5];var step=0;
            while(clock.Elapsed.TotalSeconds<9)
            {
                while(step<schedule.Length&&clock.Elapsed.TotalSeconds>=schedule[step])
                {
                    var suspend=step%2==0;var previousPhase=suspend?Phase():WpfPhase();Suspend(suspend);
                    events.Add(new{at=clock.Elapsed.TotalSeconds,suspend,previousPhase,afterPhase=suspend?WpfPhase():Phase()});step++;
                }
                var at=clock.Elapsed.TotalSeconds;var file=$"frame-{samples.Count:00000}.png";save.Invoke(window,[Path.Combine(framesPath,file),23]);
                samples.Add(new{file,elapsedSeconds=at,renderer=step%2==0?"native":"wpf",phaseEstimate=step%2==0?Phase():WpfPhase()});await Task.Delay(20);
            }
            File.WriteAllText(Path.Combine(motion,"motion-capture.json"),JsonSerializer.Serialize(new{durationSeconds=clock.Elapsed.TotalSeconds,frameCount=samples.Count,frames=samples,events,scope="Original timestamps, actual own-window screen pixels. Twenty renderer handoffs including 80ms reversals, white owned background and full opacity. Property estimates are diagnostics, not pixel measurements. Not navigation or grouped-alpha acceptance."},AtomicStore.JsonOptions));
        }
        finally{backgroundField.SetValue(window,null);background.Close();progress.Update(.5,"tracking","matrix",brush,true,30);}
        File.WriteAllText(Path.Combine(output,"phase-handoff.json"),JsonSerializer.Serialize(new{productDllSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),observations,switches,scope="Real WinRT controller frozen value, monotonic native phase estimate, actual WPF animated property and isolated PixelProgress. Screen capture separately records actual compositor result. Not ordinary preview or actual mouse acceptance."},AtomicStore.JsonOptions));
    }
}
