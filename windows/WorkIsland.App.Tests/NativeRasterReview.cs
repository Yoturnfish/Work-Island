using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunNativeRasterReview(IslandWindow window,string output,Action<string,bool,object?> check,bool backendOnly=false)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;var border=Field<Border>("OuterBorder");var progress=Field<PixelProgress>("GoalProgress");
        settings.HasCompletedOnboarding=true;settings.Rules=[new("raster-review.exe","Raster fixture")];settings.ReduceMotion=false;settings.BackgroundStyle="black";settings.BackgroundOpacity=1;settings.GoalSeconds=14400;
        var now=DateTimeOffset.Now;engine.Data.Sessions.Clear();engine.Data.Sessions.Add(new(){ApplicationIdentifier="raster-review.exe",ApplicationName="Raster fixture",DayKey=LogicalDay.Key(now,engine.Zone,settings.DayResetMinutes),StartedAt=now.AddHours(-2),EndedAt=now,EffectiveSeconds=7200});
        Field<DispatcherTimer>("fullscreenTimer").Stop();window.Left=320;window.Top=260;window.Topmost=true;
        engine.ResetObservation();engine.Observe(now,new("raster-review.exe","Raster fixture",0));Call("ApplyMaterial");Call("NotifyPointer",false);Call("Render");await Task.Delay(450);Call("EnableNativeForeground");await Task.Delay(100);
        var background=new Window{Title="Work Island isolated raster reference",ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Background=Brushes.White,Left=250,Top=190,Width=620,Height=300};background.Show();typeof(IslandWindow).GetField("fidelityBackground",Flags)!.SetValue(window,background);
        var save=typeof(IslandWindow).GetMethod("SaveScreenRegion",Flags)!;var dpi=VisualTreeHelper.GetDpi(window);var rows=new List<object>();
        var syncing=typeof(PixelProgress).GetField("nativeSyncing",Flags)!;
        var enabled=typeof(IslandWindow).GetField("nativeForegroundEnabled",Flags)!;
        void Invalidate(DependencyObject visual){if(visual is UIElement element)element.InvalidateVisual();for(var i=0;i<VisualTreeHelper.GetChildrenCount(visual);i++)Invalidate(VisualTreeHelper.GetChild(visual,i));}
        var scan=(TranslateTransform)typeof(PixelProgress).GetField("scan",Flags)!.GetValue(progress)!;var band=(System.Windows.Shapes.Rectangle)typeof(PixelProgress).GetField("filledBand",Flags)!.GetValue(progress)!;
        void SaveBitmap(BitmapSource bitmap,string path){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);}
        try
        {
            foreach(var logicalScale in new[]{.75,.82,1d})foreach(var style in backendOnly?new[]{"matrix","waveform"}:new[]{"matrix"})foreach(var alpha in backendOnly?new[]{1d,.8,.5,.25}:new[]{1d})
            {
                Call("SuspendNativeForeground");settings.IslandScale=logicalScale;settings.ProgressStyle=style;Call("ApplyScale");Call("Render");await Task.Delay(150);Call("SetPresentedWindowOpacity",alpha);Call("RefreshNativeForeground");await Task.Delay(100);
                check($"{logicalScale}/{style}/{alpha} production native tracking ready",Field<bool>("nativeForegroundActive"),Field<string?>("nativeForegroundFailure"));
                var host=typeof(PixelProgress).GetField("nativeScan",Flags)!.GetValue(progress)!;
                host.GetType().GetMethod("FreezePhaseForReview",Flags)!.Invoke(host,[.32f]);syncing.SetValue(progress,true);await Task.Delay(100);
                var stem=$"scale-{logicalScale:0.00}-{style}-alpha-{alpha:0.00}";var productionPath=Path.Combine(output,stem+"-production.png");save.Invoke(window,[productionPath,23]);
                Call("SuspendNativeForeground");enabled.SetValue(window,false);
                var clock=(AnimationClock?)typeof(PixelProgress).GetField("scanClock",Flags)!.GetValue(progress);clock?.Controller?.Remove();scan.BeginAnimation(TranslateTransform.XProperty,null);scan.X=-band.Width+.32*(progress.ActualWidth+band.Width);await Task.Delay(100);
                var referencePath=Path.Combine(output,stem+"-wpf.png");save.Invoke(window,[referencePath,23]);
                var maskVisual=new DrawingVisual();using(var dc=maskVisual.RenderOpen())dc.DrawRoundedRectangle(Brushes.White,null,new Rect(border.RenderSize),border.CornerRadius.TopLeft,border.CornerRadius.TopLeft);
                var mask=new RenderTargetBitmap((int)Math.Round(border.ActualWidth*dpi.DpiScaleX),(int)Math.Round(border.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);mask.Render(maskVisual);var maskPixels=new byte[mask.PixelWidth*mask.PixelHeight*4];mask.CopyPixels(maskPixels,mask.PixelWidth*4,0);SaveBitmap(mask,Path.Combine(output,stem+"-mask.png"));
                object Compare(string path,string? comparisonReference=null)
                {
                    using var actual=new System.Drawing.Bitmap(path);using var reference=new System.Drawing.Bitmap(comparisonReference??referencePath);var padding=(int)Math.Ceiling(23*dpi.DpiScaleX);double edgeSum=0,coreSum=0;var edges=0;var cores=0;var changed=0;var peak=0;var signed=0d;
                    for(var py=0;py<mask.PixelHeight;py++)for(var px=0;px<mask.PixelWidth;px++)
                    {
                        var a=actual.GetPixel(px+padding,py+padding);var b=reference.GetPixel(px+padding,py+padding);var dr=Math.Abs(a.R-b.R);var dg=Math.Abs(a.G-b.G);var db=Math.Abs(a.B-b.B);var delta=Math.Max(dr,Math.Max(dg,db));var sum=dr+dg+db;var coverage=maskPixels[(py*mask.PixelWidth+px)*4+3];
                        if(coverage>0&&coverage<255){edgeSum+=sum;edges++;if(delta>12)changed++;peak=Math.Max(peak,delta);signed+=(a.R-b.R+a.G-b.G+a.B-b.B)/3d;}else if(coverage==255){coreSum+=sum;cores++;}
                    }
                    return new{edgeMean=edgeSum/(edges*3),edgeChangedFraction=(double)changed/edges,edgePeak=peak,edgeSigned=signed/edges,edgeCount=edges,coreMean=coreSum/(cores*3)};
                }
                rows.Add(new{logicalScale,style,alpha,mode="production",metrics=Compare(productionPath),path=productionPath});
                if(backendOnly)
                {
                    var backendTarget=((HwndSource)PresentationSource.FromVisual(window)!).CompositionTarget;var priorMode=backendTarget.RenderMode;
                    try
                    {
                        backendTarget.RenderMode=RenderMode.SoftwareOnly;Invalidate(border);await Task.Delay(200);
                        var softwarePath=Path.Combine(output,stem+"-wpf-software.png");save.Invoke(window,[softwarePath,23]);
                        check(stem+" software reference is live WPF with native disabled",!Field<bool>("nativeForegroundActive")&&!Field<bool>("nativeForegroundEnabled")&&border.Opacity==1&&window.Opacity==alpha,new{nativeActive=Field<bool>("nativeForegroundActive"),nativeEnabled=Field<bool>("nativeForegroundEnabled"),borderOpacity=border.Opacity,windowOpacity=window.Opacity});
                        var comparison=Compare(productionPath,softwarePath);var metric=JsonSerializer.SerializeToElement(comparison);
                        check(stem+" isolated software WPF reference matches native edge",metric.GetProperty("edgeMean").GetDouble()<2&&metric.GetProperty("edgeChangedFraction").GetDouble()<.05,comparison);
                        rows.Add(new{logicalScale,style,alpha,mode="wpf-software-reference",metrics=Compare(softwarePath),nativeVsSoftware=comparison,path=softwarePath,priorRenderMode=priorMode.ToString(),renderTier=RenderCapability.Tier>>16});
                    }
                    finally{backendTarget.RenderMode=priorMode;Invalidate(border);await Task.Delay(100);syncing.SetValue(progress,false);enabled.SetValue(window,true);}
                    continue;
                }
                border.Opacity=0;await Task.Delay(100);var underlayPath=Path.Combine(output,stem+"-underlay.png");save.Invoke(window,[underlayPath,23]);border.Opacity=1;await Task.Delay(60);
                foreach(var sample in new[]{1,2,4})
                {
                    // Controlled candidate only: capture the exact same live visual,
                    // upload via the unchanged production D2D surface, no new HWND.
                    border.Opacity=1;window.UpdateLayout();
                    var bitmap=new RenderTargetBitmap((int)Math.Round(border.ActualWidth*dpi.DpiScaleX*sample),(int)Math.Round(border.ActualHeight*dpi.DpiScaleY*sample),96*dpi.DpiScaleX*sample,96*dpi.DpiScaleY*sample,PixelFormats.Pbgra32);bitmap.Render(border);
                    SaveBitmap(bitmap,Path.Combine(output,stem+$"-texture-{sample}x.png"));
                    syncing.SetValue(progress,false);enabled.SetValue(window,true);Call("RefreshNativeForeground");
                    host.GetType().GetMethod("FreezePhaseForReview",Flags)!.Invoke(host,[.32f]);syncing.SetValue(progress,true);
                    var surface=Field<object>("nativeForegroundSurface");surface.GetType().GetMethod("Upload",Flags)!.Invoke(surface,[bitmap,border.RenderSize]);surface.GetType().GetMethod("SetVisible",Flags)!.Invoke(surface,[true]);await Task.Delay(100);
                    check($"{logicalScale}/{sample}x diagnostic plane active and visible",Field<bool>("nativeForegroundActive")&&(float)host.GetType().GetProperty("PresentationOpacity",Flags)!.GetValue(host)!>.99,null);
                    var path=Path.Combine(output,stem+$"-sample-{sample}x.png");save.Invoke(window,[path,23]);rows.Add(new{logicalScale,mode=$"sample-{sample}x",metrics=Compare(path),path});
                    if(sample==1)
                    {
                        using var texture=new System.Drawing.Bitmap(Path.Combine(output,stem+"-texture-1x.png"));using var underlay=new System.Drawing.Bitmap(underlayPath);using var native=new System.Drawing.Bitmap(path);using var predicted=new System.Drawing.Bitmap(underlay);
                        var padding=(int)Math.Ceiling(23*dpi.DpiScaleX);double error=0;var edges=0;var peak=0;
                        for(var py=0;py<mask.PixelHeight;py++)for(var px=0;px<mask.PixelWidth;px++)
                        {
                            var t=texture.GetPixel(px,py);var b=underlay.GetPixel(px+padding,py+padding);var actual=native.GetPixel(px+padding,py+padding);var a=t.A/255d;
                            var p=System.Drawing.Color.FromArgb((int)Math.Round(t.R*a+b.R*(1-a)),(int)Math.Round(t.G*a+b.G*(1-a)),(int)Math.Round(t.B*a+b.B*(1-a)));predicted.SetPixel(px+padding,py+padding,p);
                            var coverage=maskPixels[(py*mask.PixelWidth+px)*4+3];if(coverage==0||coverage==255)continue;
                            var dr=Math.Abs(actual.R-p.R);var dg=Math.Abs(actual.G-p.G);var db=Math.Abs(actual.B-p.B);error+=dr+dg+db;edges++;peak=Math.Max(peak,Math.Max(dr,Math.Max(dg,db)));
                        }
                        var predictedPath=Path.Combine(output,stem+"-source-over-underlay.png");predicted.Save(predictedPath,System.Drawing.Imaging.ImageFormat.Png);
                        rows.Add(new{logicalScale,mode="source-over-underlay",metrics=Compare(predictedPath),path=predictedPath,nativeVsPredictionEdgeMean=error/(edges*3),nativeVsPredictionEdgePeak=peak,note="Independent PNG source-over reconstruction at 1:1 pixel indices, not interpolation-aware; fractional texture stretch can contribute."});
                    }
                    Call("SuspendNativeForeground");enabled.SetValue(window,false);await Task.Delay(50);
                }
                syncing.SetValue(progress,false);
                var target=((HwndSource)PresentationSource.FromVisual(window)!).CompositionTarget;var priorRenderMode=target.RenderMode;
                try
                {
                    target.RenderMode=RenderMode.SoftwareOnly;Invalidate(border);await Task.Delay(200);
                    var softwarePath=Path.Combine(output,stem+"-wpf-software.png");save.Invoke(window,[softwarePath,23]);
                    rows.Add(new{logicalScale,mode="wpf-software-reference",metrics=Compare(softwarePath),nativeVsSoftware=Compare(productionPath,softwarePath),path=softwarePath,priorRenderMode=priorRenderMode.ToString(),renderTier=RenderCapability.Tier>>16});
                }
                finally{target.RenderMode=priorRenderMode;Invalidate(border);await Task.Delay(100);enabled.SetValue(window,true);}
            }
            check("raster diagnostic preserves production number font",Field<TextBlock>("TimeLabel").FontFamily.Source=="Consolas"&&Field<TextBlock>("TimeLabel").FontSize==14,null);
        }
        finally
        {
            syncing.SetValue(progress,false);enabled.SetValue(window,true);Call("SuspendNativeForeground");typeof(IslandWindow).GetField("fidelityBackground",Flags)!.SetValue(window,null);background.Close();
            File.WriteAllText(Path.Combine(output,"native-raster-review.json"),JsonSerializer.Serialize(new{productDllSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),dpi,backendOnly,rows,scope="Diagnostic measurements, not product pixel parity acceptance: isolated tracking HWND/data, synthetic observation, actual screen captures, owned white backdrop, fixed scan phase. Unchanged production DLL. Sampling submissions and software-only HWND reference are explicit test-only overrides. The original default-hardware WPF edge quality gate is not replaced or declared passed; no real input/browser/game/hardware or deployment claim."},AtomicStore.JsonOptions));
        }
    }
}
