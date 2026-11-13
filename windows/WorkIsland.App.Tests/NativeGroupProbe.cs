using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    // Integration experiment: static WPF foreground and native scan in one group,
    // then apply group alpha. Not wired to ordinary product startup or input.
    private static async Task RunNativeGroupProbe(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        var progress=Field<PixelProgress>("GoalProgress");var border=Field<Border>("OuterBorder");var engine=Field<TrackingEngine>("engine");var brush=Field<SolidColorBrush>("statusBrush");
        window.Left=320;window.Top=260;
        engine.Data.Settings.Rules=[new("group-probe.exe","Group probe")];engine.Data.Settings.ReduceMotion=false;engine.Data.Settings.BackgroundStyle="black";engine.Data.Settings.BackgroundOpacity=1;
        engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new("group-probe.exe","Group probe",0));typeof(IslandWindow).GetMethod("Render",Flags|BindingFlags.DeclaredOnly)!.Invoke(window,null);await Task.Delay(450);window.Opacity=1;
        // Materialize the XAML image-decoder context first, but retain an
        // independent desktop compositor for the HWND target and scan.
        typeof(PixelProgress).GetMethod("EnableNativeScan",Flags)!.Invoke(progress,[window,Field<Grid>("PageHost")]);progress.Update(.5,"tracking","matrix",brush,false,30);await Task.Delay(100);
        var native=typeof(PixelProgress).GetField("nativeScan",Flags)!.GetValue(progress)??throw new InvalidOperationException("Native scan unavailable: "+typeof(PixelProgress).GetField("nativeFailure",Flags)!.GetValue(progress));
        object? Call(string name,params object?[] values)=>native.GetType().GetMethod(name,Flags)!.Invoke(native,values);
        var syncing=typeof(PixelProgress).GetField("nativeSyncing",Flags)!;var presentation=typeof(PixelProgress).GetField("nativePresentation",Flags)!;
        var scan=(TranslateTransform)typeof(PixelProgress).GetField("scan",Flags)!.GetValue(progress)!;
        var filledScan=(Canvas)typeof(PixelProgress).GetField("filledScan",Flags)!.GetValue(progress)!;var unfilledScan=(Canvas)typeof(PixelProgress).GetField("unfilledScan",Flags)!.GetValue(progress)!;
        var band=(System.Windows.Shapes.Rectangle)typeof(PixelProgress).GetField("filledBand",Flags)!.GetValue(progress)!;
        var background=new Window{ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Left=window.Left-100,Top=window.Top-100,Width=window.Width+200,Height=window.Height+200,Background=Brushes.White};
        var backgroundField=typeof(IslandWindow).GetField("fidelityBackground",Flags)!;var save=typeof(IslandWindow).GetMethod("SaveScreenRegion",Flags)!;
        var reports=new List<object>();var dpi=VisualTreeHelper.GetDpi(window);
        background.Show();backgroundField.SetValue(window,background);
        try
        {
            foreach(var style in new[]{"matrix","waveform"})
            {
                syncing.SetValue(progress,false);border.Opacity=1;window.Opacity=1;progress.Update(.5,"tracking",style,brush,false,30);window.UpdateLayout();
                filledScan.Visibility=unfilledScan.Visibility=Visibility.Collapsed;Call("SetScanning",true);typeof(PixelProgress).GetMethod("SyncNativePresentation",Flags)!.Invoke(progress,null);
                syncing.SetValue(progress,true);
                var bitmap=new RenderTargetBitmap((int)Math.Round(border.ActualWidth*dpi.DpiScaleX),(int)Math.Round(border.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);bitmap.Render(border);
                var png=Path.Combine(output,style+"-static-foreground.png");var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(png))encoder.Save(stream);
                using var surface=new NativeDrawingSnapshot(native);var decoded=await surface.Load(png,new Size(border.ActualWidth,border.ActualHeight));
                check($"{style} static WPF foreground decodes into a native composition surface",decoded.Width==bitmap.PixelWidth&&decoded.Height==bitmap.PixelHeight,new{decoded.Width,decoded.Height,bitmap.PixelWidth,bitmap.PixelHeight});
                foreach(var alpha in new[]{1d,.8,.5,.25})
                {
                    window.Opacity=1;border.Opacity=0;filledScan.Visibility=unfilledScan.Visibility=Visibility.Collapsed;
                    Call("SetScanning",true);Call("SetOpacity",alpha);Call("FreezePhaseForReview",.32f);surface.Commit();await Task.Delay(160);
                    var stem=$"{style}-alpha-{alpha:0.00}";var nativePath=Path.Combine(output,stem+"-group.png");save.Invoke(window,[nativePath,23]);var nativeWindowOpacity=window.Opacity;var nativeMouseOver=window.IsMouseOver;
                    Call("SetScanning",false);Call("SetOpacity",0d);surface.Commit();border.Opacity=1;window.Opacity=alpha;
                    scan.BeginAnimation(TranslateTransform.XProperty,null);scan.X=-band.Width+(progress.ActualWidth+band.Width)*.32;
                    filledScan.Visibility=unfilledScan.Visibility=Visibility.Visible;await Task.Delay(160);
                    var wpfPath=Path.Combine(output,stem+"-wpf.png");save.Invoke(window,[wpfPath,23]);var referenceWindowOpacity=window.Opacity;var referenceMouseOver=window.IsMouseOver;
                    using var actual=new System.Drawing.Bitmap(nativePath);using var expected=new System.Drawing.Bitmap(wpfPath);
                    var windowOrigin=window.PointToScreen(new Point());var progressOrigin=progress.PointToScreen(new Point());
                    var roi=new Rect(Math.Ceiling(23*dpi.DpiScaleX)+progressOrigin.X-windowOrigin.X,Math.Ceiling(23*dpi.DpiScaleY)+progressOrigin.Y-windowOrigin.Y,progress.ActualWidth*dpi.DpiScaleX,progress.ActualHeight*dpi.DpiScaleY);
                    double sum=0,roiSum=0;int count=0,changed=0,peak=0;
                    for(var y=0;y<actual.Height;y++)for(var x=0;x<actual.Width;x++)
                    {
                        var a=actual.GetPixel(x,y);var b=expected.GetPixel(x,y);var delta=Math.Max(Math.Abs(a.R-b.R),Math.Max(Math.Abs(a.G-b.G),Math.Abs(a.B-b.B)));
                        var channels=Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B);sum+=channels;
                        if(roi.Contains(x,y)){roiSum+=channels;count++;peak=Math.Max(peak,delta);if(delta>12)changed++;}
                    }
                    var mean=sum/(actual.Width*actual.Height*3);var roiMean=roiSum/(count*3);var changedFraction=(double)changed/count;
                    reports.Add(new{style,alpha,nativeWindowOpacity,referenceWindowOpacity,nativeMouseOver,referenceMouseOver,meanChannelDifference=mean,progressMeanDifference=roiMean,progressChangedFraction=changedFraction,progressPeakDifference=peak,nativePath,wpfPath});
                    check($"grouped {style} scan matches WPF reference at alpha {alpha}",roiMean<1&&changedFraction<.02,new{mean,roiMean,changedFraction,peak});
                    filledScan.Visibility=unfilledScan.Visibility=Visibility.Collapsed;
                }
            }
            File.WriteAllText(Path.Combine(output,"native-group-pixels.json"),JsonSerializer.Serialize(new{reports,scope="Actual screen capture of controlled IslandWindow over owned white background. Static WPF bitmap and native scan grouped BEFORE opacity; fixed phase .32. Experimental only: no input, live snapshot refresh, glass background, transitions, resources or delivery acceptance."},AtomicStore.JsonOptions));
        }
        finally
        {
            border.Opacity=1;window.Opacity=1;syncing.SetValue(progress,false);presentation.SetValue(progress,null);backgroundField.SetValue(window,null);background.Close();
            progress.Update(.5,"tracking","matrix",brush,true,30);
        }
    }
}

// Image decoding via the installed WinRT API, not a downloaded rendering runtime.
// Own-process prototype only; APIs verified in native-snapshot-*-abi-20261108.txt.
