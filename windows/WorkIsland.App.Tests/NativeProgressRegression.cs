using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunNativeProgressResource(IslandWindow window,string output)
    {
        var progress=(PixelProgress)typeof(IslandWindow).GetField("GoalProgress",Flags)!.GetValue(window)!;
        var clip=(FrameworkElement)typeof(IslandWindow).GetField("PageHost",Flags)!.GetValue(window)!;
        var engine=(TrackingEngine)typeof(IslandWindow).GetField("engine",Flags)!.GetValue(window)!;
        var brush=(SolidColorBrush)typeof(IslandWindow).GetField("statusBrush",Flags)!.GetValue(window)!;
        engine.Data.Settings.Rules=[new("native-resource.exe","Native resource")];engine.Data.Settings.ReduceMotion=false;
        engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new("native-resource.exe","Native resource",0));
        typeof(IslandWindow).GetMethod("Render",Flags|BindingFlags.DeclaredOnly)!.Invoke(window,null);await Task.Delay(500);window.Opacity=1;
        typeof(PixelProgress).GetMethod("EnableNativeScan",Flags)!.Invoke(progress,[window,clip]);
        progress.Update(.5,"tracking","matrix",brush,false,30);await Task.Delay(500);
        var native=typeof(PixelProgress).GetField("nativeScan",Flags)!.GetValue(progress)??throw new InvalidOperationException("Native backend unavailable.");
        void Native(string method,params object?[] values)=>native.GetType().GetMethod(method,Flags)!.Invoke(native,values);
        var scan=(TranslateTransform)typeof(PixelProgress).GetField("scan",Flags)!.GetValue(progress)!;
        var filledScan=(Canvas)typeof(PixelProgress).GetField("filledScan",Flags)!.GetValue(progress)!;var unfilledScan=(Canvas)typeof(PixelProgress).GetField("unfilledScan",Flags)!.GetValue(progress)!;
        var band=(System.Windows.Shapes.Rectangle)typeof(PixelProgress).GetField("filledBand",Flags)!.GetValue(progress)!;
        using var own=Process.GetCurrentProcess();var samples=new System.Collections.Generic.List<object>();
        for(var round=0;round<3;round++)foreach(var phase in new[]{"native","wpf"})
        {
            scan.BeginAnimation(TranslateTransform.XProperty,null);var useNative=phase=="native";
            Native("SetScanning",useNative);typeof(PixelProgress).GetMethod("SyncNativePresentation",Flags)!.Invoke(progress,null);
            filledScan.Visibility=unfilledScan.Visibility=useNative?Visibility.Collapsed:Visibility.Visible;
            if(!useNative)scan.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(-band.Width,progress.ActualWidth,TimeSpan.FromSeconds(2.8)){RepeatBehavior=RepeatBehavior.Forever});
            await Task.Delay(700);own.Refresh();var cpu=own.TotalProcessorTime.TotalSeconds;var watch=Stopwatch.StartNew();await Task.Delay(4000);own.Refresh();watch.Stop();var consumed=own.TotalProcessorTime.TotalSeconds-cpu;
            samples.Add(new{round,phase,cpuSeconds=consumed,seconds=watch.Elapsed.TotalSeconds,oneCorePercent=100*consumed/watch.Elapsed.TotalSeconds});
        }
        File.WriteAllText(Path.Combine(output,"native-progress-resource.json"),JsonSerializer.Serialize(new{completed=true,samples,productDllSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),scope="Same isolated actual IslandWindow, static WPF geometry AND glow retained in both phases. Window opacity 1, matrix .5, 2.8s scan; no frame callbacks or screen captures during measurement. Own-process CPU only, not DWM/GPU/long-run acceptance. Half-opacity visual failure remains unresolved; this is not delivery approval."},AtomicStore.JsonOptions));
    }
    private static async Task RunNativeProgressChecks(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var progress=Field<PixelProgress>("GoalProgress");var engine=Field<TrackingEngine>("engine");
        var compact=Field<FrameworkElement>("CompactPanel");var brush=Field<SolidColorBrush>("statusBrush");
        object? Pixel(string name)=>typeof(PixelProgress).GetField(name,Flags)!.GetValue(progress);
        object? Native()=>Pixel("nativeScan");
        object? NativeCall(string name,params object?[] values)=>Native()!.GetType().GetMethod(name,Flags)!.Invoke(Native(),values);
        T NativeValue<T>(string name)=>(T)Native()!.GetType().GetProperty(name,Flags)!.GetValue(Native())!;
        engine.Data.Settings.HasCompletedOnboarding=true;engine.Data.Settings.ReduceMotion=false;engine.Data.Settings.BackgroundStyle="black";engine.Data.Settings.BackgroundOpacity=1;
        engine.Data.Settings.Rules=[new("native-progress.exe","Native progress")];engine.Data.Settings.Websites=[];
        engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new("native-progress.exe","Native progress",0));Call("Render");await Task.Delay(450);
        typeof(PixelProgress).GetMethod("EnableNativeScan",Flags)!.Invoke(progress,[window,Field<Grid>("PageHost")]);
        progress.Update(.5,"tracking","matrix",brush,false,30);await Task.Delay(100);
        var failure=(string?)Pixel("nativeFailure");check("native progress attaches after existing backdrop dispatcher queue",Native() is not null&&failure is null,failure);
        if(Native() is null)return;
        check("native scan replaces only WPF scan clocks while retaining filled-cell glow",progress.ScanAnimating&&!((TranslateTransform)Pixel("scan")!).HasAnimatedProperties&&((System.Windows.Shapes.Path)Pixel("filled")!).Effect is DropShadowEffect{BlurRadius:5,Opacity:.42},null);
        check("native matrix uses all ninety rounded cells",NativeValue<int>("CellCount")==90,null);
        var starts=NativeValue<int>("ScanStarts");progress.Update(.6,"tracking","matrix",brush,false,30);
        check("progress fill changes retain native scan phase without restart",NativeValue<int>("ScanStarts")==starts,null);
        var original=brush.Color;brush.Color=Colors.Cyan;
        check("color changes retain native scan phase without restart",NativeValue<int>("ScanStarts")==starts,null);brush.Color=original;
        Call("ToggleExpanded",null!,new RoutedEventArgs());await Task.Delay(70);
        check("native scan remains active during visible compact fade",compact.Opacity>0&&compact.Opacity<1&&progress.ScanAnimating,null);
        await Task.Delay(400);check("hidden compact stops native scan",!compact.IsVisible&&!progress.ScanAnimating,null);
        Call("CollapseToTimer");await Task.Delay(65);check("incoming compact resumes native scan during fade",compact.IsVisible&&compact.Opacity>0&&compact.Opacity<1&&progress.ScanAnimating,null);
        Call("ToggleExpanded",null!,new RoutedEventArgs());await Task.Delay(45);Call("CollapseToTimer");await Task.Delay(400);
        check("rapid reversal settles with one active native scan",compact.IsVisible&&compact.Opacity==1&&progress.ScanAnimating&&Native() is not null,null);
        progress.Update(.5,"tracking","waveform",brush,false,30);check("native wave uses all thirty-seven bars",NativeValue<int>("CellCount")==37,null);
        progress.Update(.5,"tracking","matrix",brush,true,30);check("reduce motion stops native and WPF scan",!progress.ScanAnimating,null);
        progress.Update(.5,"tracking","matrix",brush,false,30);
        check("resuming after reduce motion restores the actual native visual opacity",NativeValue<float>("PresentationOpacity")>.99,null);
        NativeCall("FreezePhaseForReview",.32f);await Task.Delay(200);
        check("native animation controller accepts a frozen intermediate review phase",NativeValue<bool>("Active"),null);
        await RunNativeScanPixelComparison(window,progress,brush,output,check);
        File.WriteAllText(Path.Combine(output,"native-bridge-state.json"),JsonSerializer.Serialize(new{scanStarts=NativeValue<int>("ScanStarts"),cells=NativeValue<int>("CellCount"),nativeFailure=Pixel("nativeFailure"),window=window.Title,scope="Explicit opt-in on actual isolated IslandWindow, after HostBackdrop initialized. WPF static geometry/glow preserved; no external input or real-game acceptance."},AtomicStore.JsonOptions));
        await Task.Delay(1200);
    }
    private static async Task RunNativeScanPixelComparison(IslandWindow window,PixelProgress progress,SolidColorBrush brush,string output,Action<string,bool,object?> check)
    {
        var pixelType=typeof(PixelProgress);object? Pixel(string name)=>pixelType.GetField(name,Flags)!.GetValue(progress);
        object Native()=>Pixel("nativeScan")!;
        object? Call(string name,params object?[] args)=>Native().GetType().GetMethod(name,Flags)!.Invoke(Native(),args);
        var scan=(TranslateTransform)Pixel("scan")!;var filled=(Canvas)Pixel("filledScan")!;var unfilled=(Canvas)Pixel("unfilledScan")!;
        var band=(System.Windows.Shapes.Rectangle)Pixel("filledBand")!;
        var background=new Window{ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,
            Left=window.Left-100,Top=window.Top-100,Width=window.Width+200,Height=window.Height+200,Background=Brushes.White};
        var backgroundField=typeof(IslandWindow).GetField("fidelityBackground",Flags)!;
        var save=typeof(IslandWindow).GetMethod("SaveScreenRegion",Flags)!;
        var compact=(FrameworkElement)typeof(IslandWindow).GetField("CompactPanel",Flags)!.GetValue(window)!;
        var oldTransform=compact.RenderTransform;var oldOpacity=window.Opacity;var comparisons=new System.Collections.Generic.List<object>();
        background.Show();backgroundField.SetValue(window,background);
        try
        {
            foreach(var style in new[]{"matrix","waveform"})foreach(var alpha in new[]{1d,.5})
            {
                window.Opacity=alpha;progress.Update(.5,"tracking",style,brush,false,30);await Task.Delay(150);
                filled.Visibility=unfilled.Visibility=Visibility.Collapsed;Call("SetScanning",true);
                pixelType.GetMethod("SyncNativePresentation",Flags)!.Invoke(progress,null);Call("FreezePhaseForReview",.32f);await Task.Delay(120);
                var stem=$"{style}-alpha-{alpha:0.0}";var nativePath=Path.Combine(output,stem+"-native.png");save.Invoke(window,[nativePath,23]);
                Call("SetScanning",false);pixelType.GetMethod("SyncNativePresentation",Flags)!.Invoke(progress,null);scan.BeginAnimation(TranslateTransform.XProperty,null);scan.X=-band.Width+(progress.ActualWidth+band.Width)*.32;
                filled.Visibility=unfilled.Visibility=Visibility.Visible;await Task.Delay(120);
                var wpfPath=Path.Combine(output,stem+"-wpf.png");save.Invoke(window,[wpfPath,23]);
                using var native=new System.Drawing.Bitmap(nativePath);using var wpf=new System.Drawing.Bitmap(wpfPath);
                var sum=0d;var peak=0;var changed=0;var roiSum=0d;var roiChanged=0;var roiCount=0;
                var windowOrigin=window.PointToScreen(new Point(0,0));var progressOrigin=progress.PointToScreen(new Point(0,0));var dpi=VisualTreeHelper.GetDpi(window);
                var roi=new Rect(Math.Ceiling(23*dpi.DpiScaleX)+progressOrigin.X-windowOrigin.X,Math.Ceiling(23*dpi.DpiScaleX)+progressOrigin.Y-windowOrigin.Y,progress.ActualWidth*dpi.DpiScaleX,progress.ActualHeight*dpi.DpiScaleY);
                for(var y=0;y<native.Height;y++)for(var x=0;x<native.Width;x++)
                {
                    var a=native.GetPixel(x,y);var b=wpf.GetPixel(x,y);var delta=Math.Max(Math.Abs(a.R-b.R),Math.Max(Math.Abs(a.G-b.G),Math.Abs(a.B-b.B)));
                    sum+=Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B);peak=Math.Max(peak,delta);if(delta>12)changed++;
                    if(roi.Contains(x,y)){roiSum+=Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B);roiCount++;if(delta>12)roiChanged++;}
                }
                var meanChannelDifference=sum/(native.Width*native.Height*3);var changedFraction=(double)changed/(native.Width*native.Height);
                var progressMeanDifference=roiSum/(roiCount*3);var progressChangedFraction=(double)roiChanged/roiCount;
                comparisons.Add(new{style,alpha,meanChannelDifference,peakDifference=peak,changedFraction,progressMeanDifference,progressChangedFraction,nativePath,wpfPath});
                check($"actual {style} scan and WPF reference align at opacity {alpha}",progressMeanDifference<1&&progressChangedFraction<.02,new{meanChannelDifference,peak,changedFraction,progressMeanDifference,progressChangedFraction});
                if(alpha<1)
                {
                    filled.Visibility=unfilled.Visibility=Visibility.Collapsed;Call("SetScanning",true);pixelType.GetMethod("SyncNativePresentation",Flags)!.Invoke(progress,null);
                    Call("SetOpacity",1d);Call("FreezePhaseForReview",.32f);await Task.Delay(150);save.Invoke(window,[Path.Combine(output,stem+"-native-root-opaque.png"),23]);
                    Call("SetScanning",false);pixelType.GetMethod("SyncNativePresentation",Flags)!.Invoke(progress,null);
                }
                filled.Visibility=unfilled.Visibility=Visibility.Collapsed;
            }
            File.WriteAllText(Path.Combine(output,"native-pixel-comparison.json"),JsonSerializer.Serialize(new{comparisons,method="Actual screen regions over owned solid white background, same frozen .32 scan phase. Entire window plus 23 DIP margin; no resizing or external app input."},AtomicStore.JsonOptions));
        }
        finally
        {
            compact.RenderTransform=oldTransform;window.Opacity=oldOpacity;backgroundField.SetValue(window,null);background.Close();
            progress.Update(.5,"tracking","matrix",brush,true,30);progress.Update(.5,"tracking","matrix",brush,false,30);
        }
    }
}
