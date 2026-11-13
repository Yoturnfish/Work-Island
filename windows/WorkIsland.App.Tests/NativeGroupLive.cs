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
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunNativeGroupLive(IslandWindow window,string output,Action<string,bool,object?> check,bool inputReview=false)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        var progress=Field<PixelProgress>("GoalProgress");var border=Field<Border>("OuterBorder");var label=Field<TextBlock>("TimeLabel");var engine=Field<TrackingEngine>("engine");var brush=Field<SolidColorBrush>("statusBrush");
        window.Left=320;window.Top=260;
        engine.Data.Settings.Rules=[new("live-probe.exe","Live probe")];engine.Data.Settings.ReduceMotion=false;engine.Data.Settings.BackgroundStyle="black";engine.Data.Settings.BackgroundOpacity=1;
        engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new("live-probe.exe","Live probe",0));typeof(IslandWindow).GetMethod("Render",Flags|BindingFlags.DeclaredOnly)!.Invoke(window,null);await Task.Delay(450);window.Opacity=1;
        typeof(PixelProgress).GetMethod("EnableNativeScan",Flags)!.Invoke(progress,[window,Field<Grid>("PageHost")]);progress.Update(.5,"tracking","matrix",brush,false,30);await Task.Delay(150);
        var native=typeof(PixelProgress).GetField("nativeScan",Flags)!.GetValue(progress)??throw new InvalidOperationException("Native scan unavailable");
        object? Native(string method,params object?[] values)=>native.GetType().GetMethod(method,Flags)!.Invoke(native,values);
        int Starts()=>(int)native.GetType().GetProperty("ScanStarts",Flags)!.GetValue(native)!;
        var syncing=typeof(PixelProgress).GetField("nativeSyncing",Flags)!;var presentation=typeof(PixelProgress).GetField("nativePresentation",Flags)!;
        syncing.SetValue(progress,true);var dpi=VisualTreeHelper.GetDpi(window);var size=new Size(border.ActualWidth,border.ActualHeight);var updates=new List<object>();
        RenderTargetBitmap Capture()
        {
            border.Opacity=1;
            try{window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Round(size.Width*dpi.DpiScaleX),(int)Math.Round(size.Height*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);bitmap.Render(border);return bitmap;}
            finally{border.Opacity=0;}
        }
        using var surface=new NativeDrawingSnapshot(native);
        surface.Upload(Capture(),size);Native("SetOpacity",.5d);
        var starts=Starts();var initialReferences=surface.OwnedReferences;var uploadTimings=new List<double>();
        using var own=Process.GetCurrentProcess();var cpuSamples=new List<object>();
        var background=new Window{Title="Work Island input guard",ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Left=window.Left-100,Top=window.Top-100,Width=window.Width+200,Height=window.Height+200,Background=Brushes.White};
        var backgroundField=typeof(IslandWindow).GetField("fidelityBackground",Flags)!;var save=typeof(IslandWindow).GetMethod("SaveScreenRegion",Flags)!;
        NativeInputStencil? stencil=null;AdornerLayer? adorners=null;
        background.Show();backgroundField.SetValue(window,background);
        typeof(IslandWindow).GetMethod("PositionTestBackground",Flags)!.Invoke(window,null);
        try
        {
            if(inputReview)
            {
                window.Title="Work Island · 原生输入验收";
                adorners=AdornerLayer.GetAdornerLayer(border)??throw new InvalidOperationException("Missing input adorner layer");stencil=new NativeInputStencil(border);adorners.Add(stencil);window.UpdateLayout();await Task.Delay(100);
                var mouseDown=0;var clicked=false;var guardClicks=0;var button=Field<Button>("ExpandButton");
                window.PreviewMouseDown+=(_,_)=>mouseDown++;background.PreviewMouseDown+=(_,_)=>guardClicks++;
                button.Click+=(_,_)=>
                {
                    clicked=true;Native("SetScanning",false);Native("SetOpacity",0d);border.Opacity=1;syncing.SetValue(progress,false);presentation.SetValue(progress,null);
                    if(stencil is not null){adorners.Remove(stencil);stencil=null;}
                };
                var ownHandle=new WindowInteropHelper(window).Handle;var point=button.PointToScreen(new Point(button.ActualWidth/2,button.ActualHeight/2));var actualHit=LiveWindowFromPoint(new((int)Math.Round(point.X),(int)Math.Round(point.Y)));
                File.WriteAllText(Path.Combine(output,"input-ready.json"),JsonSerializer.Serialize(new{processId=own.Id,hwnd=ownHandle.ToInt64(),title=window.Title,window.ShowInTaskbar,inputTargetMatches=actualHit==ownHandle,timeoutSeconds=180,scope="Initially taskbar-visible isolated fixture for official tool targeting; one actual native click on the original ExpandButton. Optional own-fixture command hides taskbar before input. On receipt restore live WPF for navigation; no test invokes the button event."},AtomicStore.JsonOptions));
                var wait=Stopwatch.StartNew();
                while(!clicked&&wait.Elapsed.TotalSeconds<180&&!File.Exists(Path.Combine(output,"stop-review.txt")))
                {
                    if(window.ShowInTaskbar&&File.Exists(Path.Combine(output,"hide-taskbar.txt")))
                    {
                        window.ShowInTaskbar=false;await Task.Delay(100);
                        var currentHandle=new WindowInteropHelper(window).Handle;var currentTarget=LiveWindowFromPoint(new((int)Math.Round(point.X),(int)Math.Round(point.Y)));
                        File.WriteAllText(Path.Combine(output,"taskbar-hidden.json"),JsonSerializer.Serialize(new{hwnd=currentHandle.ToInt64(),sameHwnd=currentHandle==ownHandle,inputTargetMatches=currentTarget==currentHandle,window.ShowInTaskbar},AtomicStore.JsonOptions));
                    }
                    await Task.Delay(100);
                }
                if(clicked)await Task.Delay(700);
                check("actual native click reaches original expand button",clicked&&mouseDown>0&&guardClicks==0,new{clicked,mouseDown,guardClicks});
                File.WriteAllText(Path.Combine(output,"input-result.json"),JsonSerializer.Serialize(new{completed=true,clicked,mouseDown,guardClicks,nativeMouseVerified=clicked&&mouseDown>0&&guardClicks==0,window.ShowInTaskbar,expanded=Field<bool>("isExpanded"),scope="Actual native input observation only; UIA-only invocation without PreviewMouseDown does not count as mouse acceptance."},AtomicStore.JsonOptions));
                return;
            }
            for(var round=0;round<3;round++)
            {
                own.Refresh();var cpu=own.TotalProcessorTime.TotalSeconds;var watch=Stopwatch.StartNew();
                for(var second=0;second<4;second++)
                {
                    label.Text=$"0H {round*4+second+1:00}M";var upload=Stopwatch.StartNew();surface.Upload(Capture(),size);upload.Stop();uploadTimings.Add(upload.Elapsed.TotalMilliseconds);
                    await Task.Delay(1000);
                }
                own.Refresh();watch.Stop();var used=own.TotalProcessorTime.TotalSeconds-cpu;
                cpuSamples.Add(new{round,seconds=watch.Elapsed.TotalSeconds,cpuSeconds=used,oneCorePercent=100*used/watch.Elapsed.TotalSeconds});
            }
            check("live foreground uploads retain the original native scan phase",Starts()==starts,new{before=starts,after=Starts()});
            check("live uploads reuse one D3D device, surface and source bitmap",surface.DeviceCreates==1&&surface.SurfaceCreates==1&&surface.InputBitmapCreates==1&&surface.OwnedReferences==initialReferences,new{surface.Uploads,surface.DeviceCreates,surface.SurfaceCreates,surface.InputBitmapCreates,initialReferences,surface.OwnedReferences});
            var expand=Field<FrameworkElement>("ExpandButton");var center=expand.PointToScreen(new Point(expand.ActualWidth/2,expand.ActualHeight/2));var expected=new WindowInteropHelper(window).Handle;
            var hit=LiveWindowFromPoint(new((int)Math.Round(center.X),(int)Math.Round(center.Y)));
            adorners=AdornerLayer.GetAdornerLayer(border)??throw new InvalidOperationException("Missing WPF adorner layer for isolated input stencil.");
            stencil=new NativeInputStencil(border);adorners.Add(stencil);window.UpdateLayout();await Task.Delay(100);
            var protectedHit=LiveWindowFromPoint(new((int)Math.Round(center.X),(int)Math.Round(center.Y)));
            check("transparent input stencil preserves the actual Windows mouse target",protectedHit==expected,new{expected=expected.ToInt64(),withoutStencil=hit.ToInt64(),withStencil=protectedHit.ToInt64(),guard=new WindowInteropHelper(background).Handle.ToInt64()});
            var motion=Path.Combine(output,"motion");var frames=Path.Combine(motion,"frames");Directory.CreateDirectory(frames);var samples=new List<object>();var clock=Stopwatch.StartNew();var next=0;var index=0;
            while(clock.Elapsed.TotalSeconds<9)
            {
                if(clock.Elapsed.TotalSeconds>=next)
                {
                    label.Text=$"0H {20+next:00}M";var upload=Stopwatch.StartNew();surface.Upload(Capture(),size);upload.Stop();updates.Add(new{at=clock.Elapsed.TotalSeconds,milliseconds=upload.Elapsed.TotalMilliseconds,label=label.Text,scanStarts=Starts()});next++;
                }
                var at=clock.Elapsed.TotalSeconds;var name=$"frame-{index++:00000}.png";save.Invoke(window,[Path.Combine(frames,name),23]);samples.Add(new{file=name,elapsedSeconds=at});await Task.Delay(20);
            }
            File.WriteAllText(Path.Combine(motion,"motion-capture.json"),JsonSerializer.Serialize(new{durationSeconds=clock.Elapsed.TotalSeconds,frameCount=samples.Count,frames=samples,events=updates,scope="Actual own-window screen capture over owned white background, continuous native scan and one foreground texture update per second. Original timestamps; no external input."},AtomicStore.JsonOptions));
            File.WriteAllText(Path.Combine(output,"native-live.json"),JsonSerializer.Serialize(new{completed=true,productDllSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),cpuSamples,uploadMilliseconds=uploadTimings,updates,scanStartsBefore=starts,scanStartsAfter=Starts(),surface.Uploads,surface.DeviceCreates,surface.SurfaceCreates,surface.InputBitmapCreates,initialReferences,finalReferences=surface.OwnedReferences,mouseTarget=new{expected=expected.ToInt64(),withoutStencil=hit.ToInt64(),withStencil=protectedHit.ToInt64(),matches=protectedHit==expected},scope="Own isolated actual IslandWindow. WPF foreground rendered into one reusable native surface at 1Hz, native 2.8s scan continuous, root alpha .5. CPU phase has no screenshots; video phase is separate. Label data is synthetic, not real tracked minutes. Mouse target query is read-only WindowFromPoint, not actual click acceptance. The alpha-1 white input stencil is a separate non-hit-testable adorner, not part of the native foreground bitmap. No glass/transition/game/full product acceptance."},AtomicStore.JsonOptions));
        }
        finally
        {
            if(stencil is not null)adorners?.Remove(stencil);
            Native("SetScanning",false);Native("SetOpacity",0d);border.Opacity=1;window.Opacity=1;syncing.SetValue(progress,false);presentation.SetValue(progress,null);backgroundField.SetValue(window,null);background.Close();
            progress.Update(.5,"tracking","matrix",brush,true,30);
        }
    }
    [StructLayout(LayoutKind.Sequential)]private readonly record struct LivePoint(int X,int Y);
    [DllImport("user32.dll",EntryPoint="WindowFromPoint")]private static extern nint LiveWindowFromPoint(LivePoint point);
}

internal sealed class NativeInputStencil:Adorner
{
    private readonly Border border;
    private static readonly SolidColorBrush ink=CreateInk();
    private static SolidColorBrush CreateInk(){var brush=new SolidColorBrush(Color.FromArgb(1,255,255,255));brush.Freeze();return brush;}
    internal NativeInputStencil(Border border):base(border){this.border=border;IsHitTestVisible=false;}
    protected override void OnRender(DrawingContext drawing)=>drawing.DrawRoundedRectangle(ink,null,new Rect(border.RenderSize),border.CornerRadius.TopLeft,border.CornerRadius.TopLeft);
}
