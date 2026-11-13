using System;
using System.Collections.Generic;
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
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    // Actual product tracking controller. Candidate clip is an isolated explicit
    // host submission only; reference pixels are actual WPF in the same HWND.
    private static async Task RunNativeEdgeReview(IslandWindow window,string output,Action<string,bool,object?> check,bool repair,bool glass)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;var border=Field<Border>("OuterBorder");var progress=Field<PixelProgress>("GoalProgress");var page=Field<Grid>("PageHost");
        settings.HasCompletedOnboarding=true;settings.Rules=[new("edge-review.exe","Edge fixture")];settings.ReduceMotion=false;settings.BackgroundStyle=glass?"glass":"black";settings.BackgroundOpacity=1;settings.GoalSeconds=14400;
        var now=DateTimeOffset.Now;engine.Data.Sessions.Clear();engine.Data.Sessions.Add(new(){ApplicationIdentifier="edge-review.exe",ApplicationName="Edge fixture",DayKey=LogicalDay.Key(now,engine.Zone,settings.DayResetMinutes),StartedAt=now.AddHours(-2),EndedAt=now,EffectiveSeconds=7200});
        Field<DispatcherTimer>("fullscreenTimer").Stop();window.Left=320;window.Top=260;window.Topmost=true;
        engine.ResetObservation();engine.Observe(now,new("edge-review.exe","Edge fixture",0));Call("ApplyMaterial");Call("NotifyPointer",false);Call("Render");await Task.Delay(450);Call("EnableNativeForeground");await Task.Delay(100);
        var background=new Window{Title="Work Island isolated edge reference",ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Background=Brushes.White,Left=250,Top=190,Width=620,Height=300};background.Show();typeof(IslandWindow).GetField("fidelityBackground",Flags)!.SetValue(window,background);
        var save=typeof(IslandWindow).GetMethod("SaveScreenRegion",Flags)!;var dpi=VisualTreeHelper.GetDpi(window);var rows=new List<object>();
        object Native()=>typeof(PixelProgress).GetField("nativeScan",Flags)!.GetValue(progress)!;
        object? NativeCall(string name,params object?[] args)=>Native().GetType().GetMethod(name,Flags)!.Invoke(Native(),args);
        var syncing=typeof(PixelProgress).GetField("nativeSyncing",Flags)!;
        var scan=(TranslateTransform)typeof(PixelProgress).GetField("scan",Flags)!.GetValue(progress)!;var band=(System.Windows.Shapes.Rectangle)typeof(PixelProgress).GetField("filledBand",Flags)!.GetValue(progress)!;
        using var baseline=repair?JsonDocument.Parse(File.ReadAllText(Path.Combine("test-results",glass?"native-edge-glass-before-20261109-r1":"native-edge-review-20261109-r1","native-edge-review.json"))):null;
        bool Active()=>Field<bool>("nativeForegroundActive");
        try
        {
            foreach(var logicalScale in glass?new[]{.82}:new[]{.75,.82,1d})foreach(var style in new[]{"matrix","waveform"})
            {
                Call("SuspendNativeForeground");settings.IslandScale=logicalScale;settings.ProgressStyle=style;Call("ApplyScale");Call("Render");await Task.Delay(120);Call("RefreshNativeForeground");await Task.Delay(60);
                check($"{logicalScale}/{style} current tracking controller active",Active(),new{failure=Field<string?>("nativeForegroundFailure")});if(!Active())continue;
                Call("SuspendNativeForeground");
                var maskVisual=new DrawingVisual();using(var dc=maskVisual.RenderOpen())dc.DrawRoundedRectangle(Brushes.White,null,new Rect(0,0,border.ActualWidth,border.ActualHeight),border.CornerRadius.TopLeft,border.CornerRadius.TopLeft);
                var mask=new RenderTargetBitmap((int)Math.Round(border.ActualWidth*dpi.DpiScaleX),(int)Math.Round(border.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);mask.Render(maskVisual);var maskPixels=new byte[mask.PixelWidth*mask.PixelHeight*4];mask.CopyPixels(maskPixels,mask.PixelWidth*4,0);
                foreach(var alpha in new[]{1d,.8,.5,.25})
                {
                    Call("SetPresentedWindowOpacity",alpha);Call("RefreshNativeForeground");await Task.Delay(30);NativeCall("FreezePhaseForReview",.32f);syncing.SetValue(progress,true);
                    var transform=progress.TransformToAncestor(window);var o=transform.Transform(new Point());var x=transform.Transform(new Point(1,0));var y=transform.Transform(new Point(0,1));var matrix=new Matrix(x.X-o.X,x.Y-o.Y,y.X-o.X,y.Y-o.Y,o.X,o.Y);
                    var clip=(RectangleGeometry)page.Clip;var clipTransform=page.TransformToAncestor(window);var clipOrigin=clipTransform.Transform(new Point());var clipRight=clipTransform.Transform(new Point(1,0));var clipDown=clipTransform.Transform(new Point(0,1));var rect=clipTransform.TransformBounds(clip.Rect);var rx=clip.RadiusX*(clipRight.X-clipOrigin.X);var ry=clip.RadiusY*(clipDown.Y-clipOrigin.Y);
                    var stem=$"scale-{logicalScale:0.00}-{style}-alpha-{alpha:0.00}";var currentPath=Path.Combine(output,stem+"-current.png");var originalClip=typeof(PixelProgress).GetField("nativePresentation",Flags)!.GetValue(progress)?.ToString();
                    await Task.Delay(70);save.Invoke(window,[currentPath,23]);
                    var guard=rect;guard.Inflate(2,2);NativeCall("SetPresentation",matrix,alpha,new RectangleGeometry(guard,rx+2,ry+2),new Size(window.ActualWidth,window.ActualHeight),dpi);await Task.Delay(70);var candidatePath=Path.Combine(output,stem+"-guard.png");save.Invoke(window,[candidatePath,23]);
                    syncing.SetValue(progress,false);Call("SuspendNativeForeground");
                    var clock=(AnimationClock?)typeof(PixelProgress).GetField("scanClock",Flags)!.GetValue(progress);clock?.Controller?.Remove();scan.BeginAnimation(TranslateTransform.XProperty,null);scan.X=-band.Width+.32*(progress.ActualWidth+band.Width);await Task.Delay(70);
                    var referencePath=Path.Combine(output,stem+"-wpf.png");save.Invoke(window,[referencePath,23]);
                    object Compare(string path,out double edgeMean,out double edgeFraction)
                    {
                        using var actual=new System.Drawing.Bitmap(path);using var reference=new System.Drawing.Bitmap(referencePath);var padding=(int)Math.Ceiling(23*dpi.DpiScaleX);double edgeSum=0,coreSum=0,totalSum=0;var edgeCount=0;var coreCount=0;var changed=0;var peak=0;
                        for(var py=0;py<mask.PixelHeight;py++)for(var px=0;px<mask.PixelWidth;px++)
                        {
                            var a=actual.GetPixel(px+padding,py+padding);var b=reference.GetPixel(px+padding,py+padding);var dr=Math.Abs(a.R-b.R);var dg=Math.Abs(a.G-b.G);var db=Math.Abs(a.B-b.B);var delta=Math.Max(dr,Math.Max(dg,db));var sum=dr+dg+db;var coverage=maskPixels[(py*mask.PixelWidth+px)*4+3];totalSum+=sum;
                            if(coverage>0&&coverage<255){edgeSum+=sum;edgeCount++;if(delta>12)changed++;peak=Math.Max(peak,delta);}else if(coverage==255){coreSum+=sum;coreCount++;}
                        }
                        edgeMean=edgeSum/(edgeCount*3);edgeFraction=(double)changed/edgeCount;return new{edgeMeanDifference=edgeMean,edgeChangedFractionOver12=edgeFraction,edgePeakDifference=peak,edgeCount,coreMeanDifference=coreSum/(coreCount*3),windowMeanDifference=totalSum/(mask.PixelWidth*mask.PixelHeight*3)};
                    }
                    var current=Compare(currentPath,out var before,out var beforeFraction);var candidate=Compare(candidatePath,out var after,out var afterFraction);
                    var currentParity=before<2&&beforeFraction<.05;var candidateParity=after<2&&afterFraction<.05;
                    rows.Add(new{logicalScale,style,alpha,dpi,originalClip,current,candidate,strictPixelParity=currentParity,candidateStrictPixelParity=candidateParity,referencePath,currentPath,candidatePath});
                    if(!repair)
                    {
                        check(stem+" current controller has no repeated edge coverage",currentParity,current);
                        check(stem+" outside guard preserves captured rounded edge",candidateParity,candidate);
                    }
                    else
                    {
                        var old=baseline!.RootElement.GetProperty("rows").EnumerateArray().Single(row=>row.GetProperty("logicalScale").GetDouble()==logicalScale&&row.GetProperty("style").GetString()==style&&row.GetProperty("alpha").GetDouble()==alpha);
                        var oldMean=old.GetProperty("current").GetProperty("edgeMeanDifference").GetDouble();var isolatedMean=old.GetProperty("candidate").GetProperty("edgeMeanDifference").GetDouble();
                        check(stem+(glass?" glass edge does not regress against the retained original":" repairs reproduced duplicate coverage by at least 60%"),(glass?before<=oldMean+.15:before<oldMean*.4)&&before<=isolatedMean+.2,new{oldMean,currentMean=before,isolatedMean,remainingStrictPixelParity=currentParity});
                        check(stem+" product output matches isolated guard without extra foreground changes",Math.Abs(before-after)<.01&&Math.Abs(beforeFraction-afterFraction)<.001,new{current,candidate});
                    }
                }
            }
            var label=Field<TextBlock>("TimeLabel");check("tracking edge review retains approved compact number typography",label.FontFamily.Source=="Consolas"&&label.FontSize==14&&label.FontWeight==FontWeights.Bold,null);
        }
        finally
        {
            syncing.SetValue(progress,false);Call("SuspendNativeForeground");typeof(IslandWindow).GetField("fidelityBackground",Flags)!.SetValue(window,null);background.Close();
            File.WriteAllText(Path.Combine(output,"native-edge-review.json"),JsonSerializer.Serialize(new{productDllSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),wpfRenderMode=((System.Windows.Interop.HwndSource)PresentationSource.FromVisual(window)!).CompositionTarget.RenderMode.ToString(),repair,material=glass?"glass":"black",rows,scope="Actual product tracking controller in isolated HWND/data, synthetic tracking observation, same-window WPF reference, actual desktop screen captures over owned white background. Outside guard is direct isolated compositor submission. Repair gates compare independently retained E6EA baseline; strictPixelParity is separately recorded. Physical DPI125%, black logical scales .75/.82/1, glass .82 with white backdrop only. No actual input, hardware DPI, patterned glass or game claim."},AtomicStore.JsonOptions));
        }
    }
}
