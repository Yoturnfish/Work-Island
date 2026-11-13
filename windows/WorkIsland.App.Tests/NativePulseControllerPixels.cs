using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunNativePulseControllerPixels(IslandWindow window,string output,Action<string,bool,object?> check,bool glass)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;var progress=Field<PixelProgress>("GoalProgress");var border=Field<Border>("OuterBorder");var glyph=Field<Grid>("StatusIcon");var scale=(ScaleTransform)glyph.RenderTransform;var unfilled=(System.Windows.Shapes.Path)typeof(PixelProgress).GetField("unfilled",Flags)!.GetValue(progress)!;
        settings.HasCompletedOnboarding=true;settings.Rules=[];settings.Websites=[];settings.ReduceMotion=false;settings.BackgroundStyle=glass?"glass":"black";settings.BackgroundOpacity=1;settings.GoalSeconds=14400;settings.AwayTimerEnabled=false;settings.SuperReminderEnabled=false;
        var now=DateTimeOffset.Now;engine.Data.Sessions.Clear();engine.Data.Sessions.Add(new(){ApplicationIdentifier="pulse-pixels.exe",ApplicationName="Pulse pixels",DayKey=LogicalDay.Key(now,engine.Zone,settings.DayResetMinutes),StartedAt=now.AddHours(-2),EndedAt=now,EffectiveSeconds=7200});
        Field<DispatcherTimer>("fullscreenTimer").Stop();window.Left=320;window.Top=260;window.Topmost=true;Call("ApplyMaterial");Call("NotifyPointer",false);Call("Render");await Task.Delay(500);Call("EnableNativeForeground");Call("EnableNativePulses");await Task.Delay(100);
        var background=new Window{Title="Work Island controlled pulse pixel background",ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Left=250,Top=190,Width=620,Height=300,Background=Brushes.White};background.Show();typeof(IslandWindow).GetField("fidelityBackground",Flags)!.SetValue(window,background);
        var save=typeof(IslandWindow).GetMethod("SaveScreenRegion",Flags)!;var dpi=VisualTreeHelper.GetDpi(window);var rows=new List<object>();var motionRows=new List<object>();var timingOrigins=new List<object>();
        object? Surface(string name)=>typeof(IslandWindow).GetField(name,Flags)!.GetValue(window);
        void Freeze(object? surface,double phase)
        {
            if(surface==null)return;
            foreach(var field in new[]{"scaleXPulse","scaleYPulse","opacityPulse"})
            {
                var pulse=surface.GetType().GetField(field,Flags)!.GetValue(surface);if(pulse==null)continue;
                var composition=(nint)pulse.GetType().GetField("composition",Flags)!.GetValue(pulse)!;var property=(nint)pulse.GetType().GetField("property",Flags)!.GetValue(pulse)!;var source=PulsePlane.Query(composition,"0bb3784c-346b-4a7c-966b-7310966553d5");
                try
                {
                    PulsePlane.Hr(PulsePlane.Fn<PulseReviewGetController>(source,6)(source,property,out var controller));if(controller==0)throw new InvalidOperationException("Production scalar controller detached before pixel capture.");
                    try{PulsePlane.Hr(PulsePlane.Fn<PulseReviewClear>(controller,12)(controller));PulsePlane.Hr(PulsePlane.Fn<PulsePlane.PutFloat>(controller,9)(controller,(float)phase));}finally{Marshal.Release(controller);}
                }
                finally{Marshal.Release(source);}
            }
        }
        void StopReferenceClocks()
        {
            Field<AnimationClock?>("statusPulseClock")?.Controller?.Remove();scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);scale.BeginAnimation(ScaleTransform.ScaleYProperty,null);
            ((AnimationClock?)typeof(PixelProgress).GetField("pulseClock",Flags)!.GetValue(progress))?.Controller?.Remove();unfilled.BeginAnimation(UIElement.OpacityProperty,null);
        }
        double Value(double phase,double from,double to)=>from+(to-from)*(.5-.5*Math.Cos(2*Math.PI*phase));
        try
        {
            foreach(var state in new[]{"away","paused"})foreach(var style in new[]{"matrix","waveform"})
            {
                settings.Paused=state=="paused";settings.ProgressStyle=style;engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new("other.exe","Other",0));Call("Render");
                if(state=="away"){typeof(IslandWindow).GetField("awaySince",Flags)!.SetValue(window,(DateTimeOffset?)DateTimeOffset.Now.AddSeconds(-11));Call("Render");}
                await Task.Delay(500);check(state+"/"+style+" production pulse controller ready",Field<bool>("nativeForegroundActive"),new{failure=Field<string?>("nativeForegroundFailure")});
                if(!Field<bool>("nativeForegroundActive"))continue;
                var center=glyph.TransformToAncestor(border).Transform(new Point(glyph.ActualWidth/2,glyph.ActualHeight/2));var stem=state+"-"+style;
                Call("SuspendNativeForeground");StopReferenceClocks();var oldOpacity=unfilled.Opacity;unfilled.Opacity=1;
                var mask=(BitmapSource)typeof(IslandWindow).GetMethod("CapturePulsePlane",Flags)!.Invoke(window,[unfilled,new Size(border.ActualWidth,border.ActualHeight),dpi,1])!;unfilled.Opacity=oldOpacity;
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(mask));using(var stream=File.Create(Path.Combine(output,stem+"-cells.png")))encoder.Save(stream);
                foreach(var alpha in new[]{1d,.8,.5,.25})foreach(var phase in new[]{0d,.25,.5})
                {
                    Call("SetPresentedWindowOpacity",alpha);Call("RefreshNativeForeground");Freeze(Surface("nativeGlyphSurface"),phase);Freeze(Surface("nativeCellsSurface"),phase);await Task.Delay(75);
                    var name=$"{stem}-alpha-{alpha:0.00}-phase-{phase:0.00}";var nativePath=Path.Combine(output,name+"-native.png");save.Invoke(window,[nativePath,23]);
                    Call("SuspendNativeForeground");StopReferenceClocks();scale.ScaleX=scale.ScaleY=Value(phase,1,state=="paused"?.86:1.08);unfilled.Opacity=state=="away"?Value(phase,style=="matrix"?.10:.18,.35):style=="matrix"?.10:.18;await Task.Delay(75);
                    var wpfPath=Path.Combine(output,name+"-wpf.png");save.Invoke(window,[wpfPath,23]);using var actual=new System.Drawing.Bitmap(nativePath);using var reference=new System.Drawing.Bitmap(wpfPath);var padding=(int)Math.Ceiling(23*dpi.DpiScaleX);
                    var glyphRoi=new Rect(padding+center.X*dpi.DpiScaleX-26,padding+center.Y*dpi.DpiScaleY-26,52,52);var origin=window.PointToScreen(new Point());var p=progress.PointToScreen(new Point());var progressRoi=new Rect(padding+p.X-origin.X,padding+p.Y-origin.Y,progress.ActualWidth*dpi.DpiScaleX,progress.ActualHeight*dpi.DpiScaleY);
                    object Compare(Rect roi,out double mean,out double changedFraction)
                    {
                        double sum=0;var count=0;var changed=0;var peak=0;
                        for(var y=0;y<actual.Height;y++)for(var x=0;x<actual.Width;x++)if(roi.Contains(x,y))
                        {
                            var a=actual.GetPixel(x,y);var b=reference.GetPixel(x,y);var dr=Math.Abs(a.R-b.R);var dg=Math.Abs(a.G-b.G);var db=Math.Abs(a.B-b.B);var delta=Math.Max(dr,Math.Max(dg,db));sum+=dr+dg+db;peak=Math.Max(peak,delta);if(delta>12)changed++;count++;
                        }
                        mean=sum/(count*3);changedFraction=(double)changed/count;return new{meanChannelDifference=mean,changedFractionOver12=changedFraction,peak,count};
                    }
                    var glyphResult=Compare(glyphRoi,out var gm,out var gf);var progressResult=Compare(progressRoi,out var pm,out var pf);var windowResult=Compare(new Rect(padding,padding,window.ActualWidth*dpi.DpiScaleX,window.ActualHeight*dpi.DpiScaleY),out var wm,out var wf);
                    rows.Add(new{state,style,alpha,phase,center,glyph=glyphResult,progress=progressResult,window=windowResult,nativePath,wpfPath});
                    check(name+" actual production pulse pixels match fixed WPF pose",gm<2&&gf<.08&&pm<1&&pf<.02&&wm<1&&wf<.02,new{glyph=glyphResult,progress=progressResult,window=windowResult});
                }
                // Finish fixed-pose setup first. A screen read immediately after
                // changing .25 to .5 still returns the previous valid .25 frame.
                // Record the real WPF->native handoff at a constant .5 instead.
                Call("SetPresentedWindowOpacity",.5);Call("StartStatusPulseClock");typeof(PixelProgress).GetMethod("StartWpfPulse",Flags)!.Invoke(progress,null);await Task.Delay(100);
                save.Invoke(window,[Path.Combine(output,stem+"-before-handoff-wpf.png"),23]);
                var folder=Path.Combine(output,stem+"-motion");Directory.CreateDirectory(folder);var watch=Stopwatch.StartNew();
                timingOrigins.Add(new{state,style,glyphElapsedSecondsAtStart=Stopwatch.GetElapsedTime(Field<long>("statusPulseEpoch")).TotalSeconds,cellsElapsedSecondsAtStart=Stopwatch.GetElapsedTime((long)typeof(PixelProgress).GetField("pulseEpoch",Flags)!.GetValue(progress)!).TotalSeconds});
                Call("RefreshNativeForeground");
                var index=0;while(watch.Elapsed.TotalSeconds<(state=="paused"?3.6:5.6))
                {
                    var at=watch.Elapsed.TotalSeconds;var file=$"frame-{index++:00000}.png";save.Invoke(window,[Path.Combine(folder,file),23]);motionRows.Add(new{state,style,elapsedSeconds=at,file=Path.Combine(stem+"-motion",file)});await Task.Delay(35);
                }
            }
        }
        finally
        {
            Call("SuspendNativeForeground");typeof(IslandWindow).GetField("fidelityBackground",Flags)!.SetValue(window,null);background.Close();
            File.WriteAllText(Path.Combine(output,"native-pulse-probe.json"),JsonSerializer.Serialize(new{productDllSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),material=glass?"glass":"black",dpi,rows,motionRows,timingOrigins,scope="Explicitly opted-in real production controller on isolated actual HWND; actual desktop pixels over owned white background (glass uses this solid backdrop only). Fixed poses pause actual attached native controllers and remove reference WPF loops. Live recording starts at constant-.5 WPF->native handoff after alpha setup is visibly committed; all handoff frames retained. Untouched real controller epochs recorded separately per component; no live scalar getters. Not ordinary-startup, actual input, GPU reset, patterned glass, hardware or game acceptance."},AtomicStore.JsonOptions));
        }
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int PulseReviewGetController(nint source,nint property,out nint controller);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int PulseReviewClear(nint controller);
}
