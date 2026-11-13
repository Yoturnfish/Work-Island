using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    // Isolated compositor prototype. Production startup and source are unchanged.
    private static async Task RunNativePulseProbe(IslandWindow window,string output,Action<string,bool,object?> check,bool pixelsOnly)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var border=Field<Border>("OuterBorder");var glyph=Field<Grid>("StatusIcon");var progress=Field<PixelProgress>("GoalProgress");var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;
        var ink=Field<SolidColorBrush>("statusBrush");var scale=(ScaleTransform)glyph.RenderTransform;
        var unfilled=(System.Windows.Shapes.Path)typeof(PixelProgress).GetField("unfilled",Flags)!.GetValue(progress)!;
        settings.Rules=[];settings.Websites=[];settings.ReduceMotion=true;settings.BackgroundStyle="black";settings.BackgroundOpacity=1;settings.AwayTimerEnabled=false;settings.SuperReminderEnabled=false;
        var now=DateTimeOffset.Now;engine.Data.Sessions.Clear();engine.Data.Sessions.Add(new(){ApplicationIdentifier="pulse-fixture.exe",ApplicationName="Pulse fixture",DayKey=LogicalDay.Key(now,engine.Zone,settings.DayResetMinutes),StartedAt=now.AddHours(-2),EndedAt=now,EffectiveSeconds=7200});
        Field<DispatcherTimer>("fullscreenTimer").Stop();window.Left=320;window.Top=260;window.Topmost=true;Call("ApplyMaterial");Call("Tick");await Task.Delay(600);
        var dpi=VisualTreeHelper.GetDpi(window);var background=new Window{Title="Work Island isolated pulse background",ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Left=window.Left-80,Top=window.Top-80,Width=window.Width+160,Height=window.Height+160,Background=Brushes.White};
        background.Show();typeof(IslandWindow).GetField("fidelityBackground",Flags)!.SetValue(window,background);
        var save=typeof(IslandWindow).GetMethod("SaveScreenRegion",Flags)!;
        var hostType=typeof(IslandWindow).Assembly.GetType("WorkIsland.App.NativeProgressScan")!;
        using var host=(IDisposable)Activator.CreateInstance(hostType,Flags,null,[new WindowInteropHelper(window).Handle],null)!;
        object? Native(string name,params object?[] values)=>hostType.GetMethod(name,Flags)!.Invoke(host,values);
        var compositor=(nint)hostType.GetField("compositor",Flags)!.GetValue(host)!;
        var surfaces=new List<PulsePlane>();var rows=new List<object>();var cpuRows=new List<object>();var motionRows=new List<object>();var queries=0;
        var source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)!;
        nint Hook(nint hwnd,int message,nint w,nint l,ref bool handled){if(message==0x3d)queries++;return 0;}
        source.AddHook(Hook);var adornerLayer=AdornerLayer.GetAdornerLayer(border)!;var stencil=new NativeInputStencil(border);var stencilAdded=false;
        void Stencil(bool value){if(stencilAdded==value)return;if(value)adornerLayer.Add(stencil);else adornerLayer.Remove(stencil);stencilAdded=value;}
        var size=new Size(border.ActualWidth,border.ActualHeight);
        BitmapSource Capture(Visual visual,bool isolated=false)
        {
            var resolution=ReferenceEquals(visual,glyph)?4:1;
            var bitmap=new RenderTargetBitmap((int)Math.Round(size.Width*dpi.DpiScaleX*resolution),(int)Math.Round(size.Height*dpi.DpiScaleY*resolution),96*dpi.DpiScaleX*resolution,96*dpi.DpiScaleY*resolution,PixelFormats.Pbgra32);
            if(!isolated)bitmap.Render(visual);
            else
            {
                var element=(FrameworkElement)visual;var drawing=new DrawingVisual();
                using(var dc=drawing.RenderOpen())
                {
                    var transform=element.TransformToAncestor(border);var origin=transform.Transform(new Point());var right=transform.Transform(new Point(1,0));var down=transform.Transform(new Point(0,1));
                    dc.PushTransform(new MatrixTransform(new Matrix(right.X-origin.X,right.Y-origin.Y,down.X-origin.X,down.Y-origin.Y,origin.X,origin.Y)));
                    var brush=new VisualBrush(visual){Stretch=Stretch.Fill,ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,0,element.ActualWidth,element.ActualHeight)};
                    dc.DrawRectangle(brush,null,new Rect(0,0,element.ActualWidth,element.ActualHeight));dc.Pop();
                }
                bitmap.Render(drawing);
            }
            bitmap.Freeze();return bitmap;
        }
        // The captured foreground already owns the rounded WPF edge coverage.
        // A second antialiased clip at that edge multiplies coverage and makes
        // a bright fringe. Keep the host guard outside those captured pixels.
        void Root(double alpha)=>Native("SetPresentation",Matrix.Identity,alpha,new RectangleGeometry(new Rect(-2,-2,window.ActualWidth+4,window.ActualHeight+4),border.CornerRadius.TopLeft+2,border.CornerRadius.TopLeft+2),new Size(window.ActualWidth,window.ActualHeight),dpi);
        void NativeFrame(double alpha){Stencil(true);window.Opacity=1;border.Opacity=0;Root(alpha);}
        void SaveBitmap(BitmapSource bitmap,string path){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);}
        double Value(double phase,double from,double to)=>from+(to-from)*(.5-.5*Math.Cos(phase*2*Math.PI));
        void StaticWpf(double phase,string state,string style)
        {
            Root(0);Stencil(false);border.Opacity=1;scale.ScaleX=scale.ScaleY=Value(phase,1,state=="paused"?.86:1.08);unfilled.Opacity=state=="away"?Value(phase,style=="matrix"?.10:.18,.35):style=="matrix"?.10:.18;
        }
        async Task Measure(string state,string style,string renderer,int round,PulsePlane basePlane)
        {
            await Task.Delay(500);queries=0;var ticks=0;var uploads=0;var tickTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
            string Stamp()=>Field<TextBlock>("TitleLabel").Text+"/"+Field<TextBlock>("TimeLabel").Text;var stamp=Stamp();
            tickTimer.Tick+=(_,_)=>
            {
                ticks++;Call("Tick");
                if(renderer=="native")
                {
                    if(stamp!=Stamp())
                    {
                        border.Opacity=1;glyph.Opacity=0;unfilled.Opacity=0;var bitmap=Capture(border);basePlane.Upload(bitmap,new Size(bitmap.PixelWidth*96/bitmap.DpiX,bitmap.PixelHeight*96/bitmap.DpiY));uploads++;stamp=Stamp();glyph.Opacity=1;unfilled.Opacity=style=="matrix"?.10:.18;
                    }
                    NativeFrame(.5);
                }
                else window.Opacity=.5;
            };
            using var own=Process.GetCurrentProcess();own.Refresh();var cpu=own.TotalProcessorTime.TotalSeconds;var allocation=TestRuntime.GetTotalAllocatedBytes(false);var watch=Stopwatch.StartNew();tickTimer.Start();
            try{await Task.Delay(4100);}finally{tickTimer.Stop();}own.Refresh();
            cpuRows.Add(new{state,style,renderer,round,elapsedSeconds=watch.Elapsed.TotalSeconds,oneCorePercent=100*(own.TotalProcessorTime.TotalSeconds-cpu)/watch.Elapsed.TotalSeconds,allocatedBytes=TestRuntime.GetTotalAllocatedBytes(false)-allocation,mainQueries=queries,ticks,uploads,window.IsActive,window.IsVisible,scope="Actual WindowsActivity/AtomicStore Tick at 1Hz. Owned manual WPF reference timelines versus native planes, same half-progress display and white background, no screenshots during timing. Foreground uploads only if title/time stamp changes. Prototype settings.ReduceMotion=true suppresses product WPF loops; the WPF reference explicitly attaches equivalent timelines."});
        }
        try
        {
            foreach(var state in new[]{"away","paused"})foreach(var style in new[]{"matrix","waveform"})
            {
                settings.Paused=state=="paused";settings.ReduceMotion=true;settings.ProgressStyle=style;Call("Tick");progress.Update(.5,state,style,ink,true,30);window.UpdateLayout();await Task.Delay(80);
                check(state+" "+style+" prototype has no WPF animation properties",!scale.HasAnimatedProperties&&!unfilled.HasAnimatedProperties,null);
                scale.ScaleX=scale.ScaleY=1;glyph.Opacity=0;unfilled.Opacity=0;var baseBitmap=Capture(border);glyph.Opacity=1;var glyphBitmap=Capture(glyph,true);unfilled.Opacity=1;var cellsBitmap=Capture(unfilled,true);unfilled.Opacity=style=="matrix"?.10:.18;
                var stem=state+"-"+style;SaveBitmap(baseBitmap,Path.Combine(output,stem+"-base.png"));SaveBitmap(glyphBitmap,Path.Combine(output,stem+"-glyph.png"));SaveBitmap(cellsBitmap,Path.Combine(output,stem+"-cells.png"));
                // Keep bitmap texels at their captured DPI. Stretching rounded
                // pixel dimensions back into a fractional layout size resamples
                // every static label and cell, even at native scale 1.
                PulsePlane Plane(BitmapSource bitmap){var plane=new PulsePlane(Native("CreateForegroundSurface")!,compositor);surfaces.Add(plane);plane.Upload(bitmap,new Size(bitmap.PixelWidth*96/bitmap.DpiX,bitmap.PixelHeight*96/bitmap.DpiY));plane.Top();return plane;}
                var basePlane=Plane(baseBitmap);var cellsPlane=Plane(cellsBitmap);var glyphPlane=Plane(glyphBitmap);
                var center=glyph.TransformToAncestor(border).Transform(new Point(glyph.ActualWidth/2,glyph.ActualHeight/2));glyphPlane.Center(center);
                using var x=glyphPlane.Animate("Scale.X",1,state=="paused"?.86:1.08,state=="paused"?1.8:5.6);
                using var y=glyphPlane.Animate("Scale.Y",1,state=="paused"?.86:1.08,state=="paused"?1.8:5.6);
                using var cellPulse=state=="away"?cellsPlane.Animate("Opacity",style=="matrix"?.10:.18,.35,2.8):null;
                if(cellPulse==null)cellsPlane.Opacity(style=="matrix"?.10:.18);
                foreach(var alpha in new[]{1d,.8,.5,.25})foreach(var phase in new[]{0d,.25,.5})
                {
                    x.Seek(phase);y.Seek(phase);cellPulse?.Seek(phase);NativeFrame(alpha);await Task.Delay(100);
                    var name=$"{stem}-alpha-{alpha:0.00}-phase-{phase:0.00}";var nativePath=Path.Combine(output,name+"-native.png");save.Invoke(window,[nativePath,23]);
                    StaticWpf(phase,state,style);window.Opacity=alpha;await Task.Delay(100);var wpfPath=Path.Combine(output,name+"-wpf.png");save.Invoke(window,[wpfPath,23]);
                    var windowOrigin=window.PointToScreen(new Point());Rect Roi(FrameworkElement element,double margin=2){var p=element.PointToScreen(new Point());return new Rect(Math.Ceiling(23*dpi.DpiScaleX)+p.X-windowOrigin.X-margin,Math.Ceiling(23*dpi.DpiScaleY)+p.Y-windowOrigin.Y-margin,element.ActualWidth*dpi.DpiScaleX+2*margin,element.ActualHeight*dpi.DpiScaleY+2*margin);}
                    var glyphRoi=Roi(glyph,4);var progressRoi=Roi(progress);using var actual=new System.Drawing.Bitmap(nativePath);using var expected=new System.Drawing.Bitmap(wpfPath);
                    object Compare(Rect roi,out double mean,out double fraction)
                    {
                        double sum=0;var count=0;var changed=0;var peak=0;
                        for(var py=0;py<actual.Height;py++)for(var px=0;px<actual.Width;px++)if(roi.Contains(px,py))
                        {
                            var a=actual.GetPixel(px,py);var b=expected.GetPixel(px,py);var dr=Math.Abs(a.R-b.R);var dg=Math.Abs(a.G-b.G);var db=Math.Abs(a.B-b.B);sum+=dr+dg+db;var delta=Math.Max(dr,Math.Max(dg,db));peak=Math.Max(peak,delta);if(delta>12)changed++;count++;
                        }
                        mean=sum/(3*count);fraction=(double)changed/count;return new{meanChannelDifference=mean,changedFractionOver12=fraction,peak,count};
                    }
                    var glyphResult=Compare(glyphRoi,out var gm,out var gf);var progressResult=Compare(progressRoi,out var pm,out var pf);
                    var windowResult=Compare(new Rect(Math.Ceiling(23*dpi.DpiScaleX),Math.Ceiling(23*dpi.DpiScaleY),window.ActualWidth*dpi.DpiScaleX,window.ActualHeight*dpi.DpiScaleY),out var wm,out var wf);
                    rows.Add(new{state,style,alpha,phase,center,glyph=glyphResult,progress=progressResult,window=windowResult,nativePath,wpfPath});
                    check(name+" fixed native glyph/cells match WPF reference",gm<2&&gf<.08&&pm<1&&pf<.02&&wm<1&&wf<.02,new{glyph=glyphResult,progress=progressResult,window=windowResult});
                }
                // Do not permit ordinary production usage on the basis of this prototype.
                NativeFrame(.5);x.Seek(0,true);y.Seek(0,true);cellPulse?.Seek(0,true);
                for(var round=0;!pixelsOnly&&round<2;round++)
                {
                    await Measure(state,style,"native",round,basePlane);x.Seek(.32);y.Seek(.32);cellPulse?.Seek(.32);StaticWpf(.32,state,style);window.Opacity=.5;
                    var iconAnimation=new DoubleAnimation(1,state=="paused"?.86:1.08,TimeSpan.FromSeconds(state=="paused"?.9:2.8)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}};
                    var iconClock=(AnimationClock)iconAnimation.CreateClock(true);scale.ApplyAnimationClock(ScaleTransform.ScaleXProperty,iconClock);scale.ApplyAnimationClock(ScaleTransform.ScaleYProperty,iconClock);
                    AnimationClock? cellsClock=null;
                    if(state=="away"){cellsClock=(AnimationClock)new DoubleAnimation(style=="matrix"?.10:.18,.35,TimeSpan.FromSeconds(1.4)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}}.CreateClock(true);unfilled.ApplyAnimationClock(UIElement.OpacityProperty,cellsClock);}
                    try{await Measure(state,style,"wpf",round,basePlane);}
                    finally{iconClock.Controller!.Remove();cellsClock?.Controller!.Remove();scale.ApplyAnimationClock(ScaleTransform.ScaleXProperty,null);scale.ApplyAnimationClock(ScaleTransform.ScaleYProperty,null);unfilled.ApplyAnimationClock(UIElement.OpacityProperty,null);}
                    NativeFrame(.5);x.Seek(0,true);y.Seek(0,true);cellPulse?.Seek(0,true);
                }
                var motion=Path.Combine(output,stem+"-motion");Directory.CreateDirectory(motion);var watch=Stopwatch.StartNew();var frame=0;
                x.Seek(0,true);y.Seek(0,true);cellPulse?.Seek(0,true);
                while(!pixelsOnly&&watch.Elapsed.TotalSeconds<(state=="paused"?3.6:5.6))
                {
                    var at=watch.Elapsed.TotalSeconds;var file=$"frame-{frame++:00000}.png";save.Invoke(window,[Path.Combine(motion,file),23]);motionRows.Add(new{state,style,elapsedSeconds=at,file=Path.Combine(stem+"-motion",file)});await Task.Delay(35);
                }
                x.Dispose();y.Dispose();cellPulse?.Dispose();Root(0);
                check(stem+" StopAnimation detaches actual native controllers",x.Stopped&&y.Stopped&&(cellPulse?.Stopped??true),new{x.ControllerAttachedAfterStop,yControllerAttachedAfterStop=y.ControllerAttachedAfterStop,cellsControllerAttachedAfterStop=cellPulse?.ControllerAttachedAfterStop});
                foreach(var plane in surfaces)plane.Dispose();check(stem+" planes release owned COM references and input bitmap",surfaces.All(plane=>plane.Released),null);surfaces.Clear();border.Opacity=1;scale.ScaleX=scale.ScaleY=1;window.Opacity=1;
            }
            File.WriteAllText(Path.Combine(output,"final-wpf-clocks.json"),JsonSerializer.Serialize(AuditControllerClocks(window),AtomicStore.JsonOptions));
        }
        finally
        {
            Native("SetOpacity",0d);foreach(var plane in surfaces)plane.Dispose();Stencil(false);source.RemoveHook(Hook);typeof(IslandWindow).GetField("fidelityBackground",Flags)!.SetValue(window,null);background.Close();border.Opacity=1;window.Opacity=1;scale.ScaleX=scale.ScaleY=1;glyph.Opacity=1;
            File.WriteAllText(Path.Combine(output,"native-pulse-probe.json"),JsonSerializer.Serialize(new{productDllSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),dpi,pixelsOnly,glyphResolutionScale=4,roundedEdgeCoverage="WPF capture owns edge alpha; native guard clip outside those pixels",rows,cpuRows,motionRows,scope="Actual isolated same-HWND three-plane prototype, owned white background, real desktop screen pixels. Scalar keyframes approximate existing WPF SineEase. No production source/startup changed. Fixed-phase pixel gates remain binding. CPU includes 1Hz actual business Tick but uses explicit equivalent WPF timelines; no real input, renderer handoff, glass, hardware/game or full delivery acceptance."},AtomicStore.JsonOptions));
        }
    }
}

// Only verified ABI slots from NativeProgressScan/NativeForegroundSurface and
// native-scan-visual-abi-20261108.txt. No external UI operations or extra HWND.
internal sealed class PulsePlane:IDisposable
{
    private readonly object surface;private readonly Type type;private readonly nint compositor;private nint composition;private bool disposed;
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    internal PulsePlane(object surface,nint compositor){this.surface=surface;type=surface.GetType();this.compositor=compositor;}
    private nint Visual=>(nint)type.GetField("imageVisual",Flags)!.GetValue(surface)!;
    internal void Upload(BitmapSource bitmap,Size size)=>type.GetMethod("Upload",Flags)!.Invoke(surface,[bitmap,size]);
    internal void Top(){var children=(nint)type.GetField("children",Flags)!.GetValue(surface)!;Hr(Fn<PutObject>(children,11)(children,Visual));Hr(Fn<PutObject>(children,9)(children,Visual));}
    internal void Center(Point point)=>Hr(Fn<PutVector3>(Visual,13)(Visual,new((float)point.X,(float)point.Y,0)));
    internal void Opacity(double value)=>Hr(Fn<PutFloat>(Visual,23)(Visual,(float)value));
    internal PulseAnimation Animate(string property,double from,double to,double seconds)
    {
        if(composition==0)composition=Query(Visual,"bcb4ad45-7609-4550-934f-16002a68fded");
        return new(compositor,composition,property,from,to,seconds);
    }
    internal bool Released=>disposed&&(int)type.GetProperty("OwnedReferences",Flags)!.GetValue(surface)! == 0;
    public void Dispose(){if(disposed)return;disposed=true;if(composition!=0){Marshal.Release(composition);composition=0;}((IDisposable)surface).Dispose();}
    internal static T Fn<T>(nint pointer,int slot)where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(pointer),slot*IntPtr.Size));
    internal static nint Query(nint pointer,string text){var id=new Guid(text);Hr(RuntimeCompat.QueryInterface(pointer,id,out var result));return result;}
    internal static void Hr(int hr)=>Marshal.ThrowExceptionForHR(hr);
    [StructLayout(LayoutKind.Sequential)]internal readonly record struct Vector3(float X,float Y,float Z);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]internal delegate int PutObject(nint self,nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]internal delegate int PutFloat(nint self,float value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]internal delegate int PutVector3(nint self,Vector3 value);
}

internal sealed class PulseAnimation:IDisposable
{
    private readonly List<nint> refs=[];private readonly nint composition;private nint name,controller;private bool started,disposed;
    internal bool? ControllerAttachedAfterStop{get;private set;}
    internal bool Stopped=>disposed&&!started&&ControllerAttachedAfterStop==false;
    internal PulseAnimation(nint compositor,nint composition,string property,double from,double to,double seconds)
    {
        this.composition=composition;
        try
        {
            PulsePlane.Hr(WindowsCreateString(property,property.Length,out name));
            nint Create(int slot){PulsePlane.Hr(PulsePlane.Fn<CreateValue>(compositor,slot)(compositor,out var value));refs.Add(value);return value;}
            nint Query(nint pointer,string id){var value=PulsePlane.Query(pointer,id);refs.Add(value);return value;}
            var scalar=Create(20);var key=Query(scalar,"126e7f22-3ae9-4540-9a8a-deae8a4a4a84");var linear=Query(Create(17),"5145e356-bf79-4ea8-8cc2-6b5b472e6c9a");
            for(var i=0;i<=128;i++){var progress=i/128f;var value=(float)(from+(to-from)*(.5-.5*Math.Cos(progress*2*Math.PI)));PulsePlane.Hr(PulsePlane.Fn<InsertKeyFrame>(scalar,7)(scalar,progress,value,linear));}
            PulsePlane.Hr(PulsePlane.Fn<PutLong>(key,9)(key,TimeSpan.FromSeconds(seconds).Ticks));PulsePlane.Hr(PulsePlane.Fn<PutInt>(key,11)(key,1));
            var animation=Query(scalar,"464c4c2c-1caa-4061-9b40-e13fde1503ca");PulsePlane.Hr(PulsePlane.Fn<Start>(composition,9)(composition,name,animation));started=true;
            var source=Query(composition,"0bb3784c-346b-4a7c-966b-7310966553d5");PulsePlane.Hr(PulsePlane.Fn<GetController>(source,6)(source,name,out controller));if(controller==0)throw new InvalidOperationException("Missing pulse animation controller");refs.Add(controller);
        }
        catch{Dispose();throw;}
    }
    internal void Seek(double phase,bool resume=false){RuntimeCompat.ThrowIfDisposed(disposed,this);PulsePlane.Hr(PulsePlane.Fn<Clear>(controller,12)(controller));PulsePlane.Hr(PulsePlane.Fn<PulsePlane.PutFloat>(controller,9)(controller,(float)phase));if(resume)PulsePlane.Hr(PulsePlane.Fn<Clear>(controller,13)(controller));}
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        try
        {
            if(started)
            {
                PulsePlane.Hr(PulsePlane.Fn<Stop>(composition,10)(composition,name));started=false;
                var source=PulsePlane.Query(composition,"0bb3784c-346b-4a7c-966b-7310966553d5");
                try{PulsePlane.Hr(PulsePlane.Fn<GetController>(source,6)(source,name,out var remaining));ControllerAttachedAfterStop=remaining!=0;if(remaining!=0)Marshal.Release(remaining);}
                finally{Marshal.Release(source);}
            }
        }
        finally{for(var i=refs.Count-1;i>=0;i--)Marshal.Release(refs[i]);refs.Clear();if(name!=0){WindowsDeleteString(name);name=0;}}
    }
    [DllImport("combase.dll",CharSet=CharSet.Unicode)]private static extern int WindowsCreateString(string text,int length,out nint value);
    [DllImport("combase.dll")]private static extern int WindowsDeleteString(nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int CreateValue(nint self,out nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int InsertKeyFrame(nint self,float progress,float value,nint easing);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int PutLong(nint self,long value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int PutInt(nint self,int value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int Start(nint self,nint property,nint animation);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int Stop(nint self,nint property);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int GetController(nint self,nint property,out nint controller);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int Clear(nint self);
}
