using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunNativeRasterFresh(string output,Action<string,bool,object?> check,bool software,bool product=false)
    {
        var rows=new List<object>();var handles=new HashSet<nint>();var allFresh=true;
        foreach(var scale in new[]{.75,.82,1d})foreach(var style in new[]{"matrix","waveform"})foreach(var alpha in new[]{1d,.8,.5,.25})
        {
            var stem=$"scale-{scale:0.00}-{style}-alpha-{alpha:0.00}";var folder=Path.Combine(output,stem);Directory.CreateDirectory(folder);
            var pointerInside=alpha==.25;
            var state=new AppState{Settings=new(){HasCompletedOnboarding=true,Rules=[new("fresh-raster.exe","Fresh raster")],IslandScale=scale,ProgressStyle=style,BackgroundStyle="black",BackgroundOpacity=pointerInside?.5:alpha,ReduceMotion=false,GoalSeconds=14400,WindowX=320,WindowY=260}};
            var window=new IslandWindow(new AtomicStore(Path.Combine(folder,"isolated-state.json")),state,false,null,startupRegistration:new IsolatedStartupRegistration(Path.Combine(folder,"startup-fixture.json"),"isolated-fresh-raster"),recognitionSource:new RecognitionFixture(),applicationPicker:new ApplicationPickerFixture(),fullscreenSource:new FullscreenFixture());
            T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
            object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
            var engine=Field<TrackingEngine>("engine");var border=Field<Border>("OuterBorder");var progress=Field<PixelProgress>("GoalProgress");Window? background=null;
            var enabled=typeof(IslandWindow).GetField("nativeForegroundEnabled",Flags)!;
            var sourceMode=software?RenderMode.SoftwareOnly:RenderMode.Default;
            if(!product)window.SourceInitialized+=(_,_)=>((HwndSource)PresentationSource.FromVisual(window)!).CompositionTarget.RenderMode=sourceMode;
            Field<DispatcherTimer>("timer").Stop();Field<DispatcherTimer>("fullscreenTimer").Stop();window.Loaded+=(_,_)=>Field<DispatcherTimer>("timer").Stop();window.ShowActivated=false;window.Topmost=true;window.Left=320;window.Top=260;
            try
            {
                window.Show();await Task.Delay(80);var hwnd=new WindowInteropHelper(window).Handle;allFresh&=handles.Add(hwnd);var actualRenderMode=((HwndSource)PresentationSource.FromVisual(window)!).CompositionTarget.RenderMode;
                var now=DateTimeOffset.Now;engine.Data.Sessions.Clear();engine.Data.Sessions.Add(new(){ApplicationIdentifier="fresh-raster.exe",ApplicationName="Fresh raster",DayKey=LogicalDay.Key(now,engine.Zone,state.Settings.DayResetMinutes),StartedAt=now.AddHours(-2),EndedAt=now,EffectiveSeconds=7200});
                engine.ResetObservation();engine.Observe(now,new("fresh-raster.exe","Fresh raster",0));Call("ApplyMaterial");Call("NotifyPointer",pointerInside);Call("Render");await Task.Delay(450);Call("NotifyPointer",pointerInside);
                background=new Window{Title="Work Island fresh raster reference",ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Background=Brushes.White,Left=250,Top=190,Width=620,Height=300};background.Show();typeof(IslandWindow).GetField("fidelityBackground",Flags)!.SetValue(window,background);
                var dpi=VisualTreeHelper.GetDpi(window);var save=typeof(IslandWindow).GetMethod("SaveScreenRegion",Flags)!;
                void FreezeWpf()
                {
                    var clock=(AnimationClock?)typeof(PixelProgress).GetField("scanClock",Flags)!.GetValue(progress);clock?.Controller?.Remove();
                    var scan=(TranslateTransform)typeof(PixelProgress).GetField("scan",Flags)!.GetValue(progress)!;var band=(System.Windows.Shapes.Rectangle)typeof(PixelProgress).GetField("filledBand",Flags)!.GetValue(progress)!;
                    scan.BeginAnimation(TranslateTransform.XProperty,null);scan.X=-band.Width+.32*(progress.ActualWidth+band.Width);
                }
                var surfaces=Field<object>("surfaces");var shadow=(Window)surfaces.GetType().GetField("shadow",Flags)!.GetValue(surfaces)!;
                FreezeWpf();await Task.Delay(100);var before=Path.Combine(folder,"wpf-before.png");save.Invoke(window,[before,23]);var beforeShadowAlpha=shadow.Opacity;
                using(var backdropCheck=new System.Drawing.Bitmap(before))
                {
                    var corner=backdropCheck.GetPixel(1,1);check(stem+" real capture has the owned white reference background",corner.R>=254&&corner.G>=254&&corner.B>=254,new{corner.R,corner.G,corner.B});
                }
                check(stem+" initial reference is WPF before native initialization",!Field<bool>("nativeForegroundEnabled")&&!Field<bool>("nativeForegroundActive")&&border.Opacity==1&&window.Opacity==alpha&&shadow.Opacity==alpha&&actualRenderMode==(product?RenderMode.SoftwareOnly:sourceMode),new{physicalAlpha=window.Opacity,shadowAlpha=shadow.Opacity,actualRenderMode=actualRenderMode.ToString()});
                Call("EnableNativeForeground");await Task.Delay(100);
                var host=typeof(PixelProgress).GetField("nativeScan",Flags)!.GetValue(progress)!;host.GetType().GetMethod("FreezePhaseForReview",Flags)!.Invoke(host,[.32f]);await Task.Delay(100);
                var actualAlpha=(float)host.GetType().GetProperty("PresentationOpacity",Flags)!.GetValue(host)!;
                var nativeShadowAlpha=shadow.Opacity;check(stem+" native controller alpha and renderer ready",Field<bool>("nativeForegroundActive")&&!Field<bool>("nativeForegroundFailed")&&Math.Abs(actualAlpha-alpha)<.00001&&window.Opacity==1&&shadow.Opacity==alpha,new{actualAlpha,logicalAlpha=alpha,shadowAlpha=shadow.Opacity});
                var native=Path.Combine(folder,"native.png");save.Invoke(window,[native,23]);
                Call("SuspendNativeForeground");enabled.SetValue(window,false);FreezeWpf();await Task.Delay(100);var after=Path.Combine(folder,"wpf-after.png");save.Invoke(window,[after,23]);
                check(stem+" returned reference is live WPF with native disabled",!Field<bool>("nativeForegroundActive")&&border.Opacity==1&&window.Opacity==alpha&&shadow.Opacity==alpha,new{shadowAlpha=shadow.Opacity});
                var maskVisual=new DrawingVisual();using(var dc=maskVisual.RenderOpen())dc.DrawRoundedRectangle(Brushes.White,null,new Rect(border.RenderSize),border.CornerRadius.TopLeft,border.CornerRadius.TopLeft);
                var mask=new RenderTargetBitmap((int)Math.Round(border.ActualWidth*dpi.DpiScaleX),(int)Math.Round(border.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);mask.Render(maskVisual);var maskPixels=new byte[mask.PixelWidth*mask.PixelHeight*4];mask.CopyPixels(maskPixels,mask.PixelWidth*4,0);
                object Compare(string actualPath,string referencePath)
                {
                    using var a=new System.Drawing.Bitmap(actualPath);using var b=new System.Drawing.Bitmap(referencePath);var padding=(int)Math.Ceiling(23*dpi.DpiScaleX);double sum=0,core=0;var count=0;var changed=0;var peak=0;var coreCount=0;
                    for(var y=0;y<mask.PixelHeight;y++)for(var x=0;x<mask.PixelWidth;x++)
                    {
                        var p=a.GetPixel(x+padding,y+padding);var q=b.GetPixel(x+padding,y+padding);var dr=Math.Abs(p.R-q.R);var dg=Math.Abs(p.G-q.G);var db=Math.Abs(p.B-q.B);var delta=Math.Max(dr,Math.Max(dg,db));var coverage=maskPixels[(y*mask.PixelWidth+x)*4+3];
                        if(coverage>0&&coverage<255){sum+=dr+dg+db;count++;if(delta>12)changed++;peak=Math.Max(peak,delta);}else if(coverage==255){core+=dr+dg+db;coreCount++;}
                    }
                    return new{edgeMean=sum/(count*3),edgeChangedFraction=(double)changed/count,edgePeak=peak,edgeCount=count,coreMean=core/(coreCount*3)};
                }
                var nativeVsBefore=Compare(native,before);var nativeVsAfter=Compare(native,after);var beforeVsAfter=Compare(before,after);
                if(product)
                {
                    var a=JsonSerializer.SerializeToElement(nativeVsBefore);var b=JsonSerializer.SerializeToElement(nativeVsAfter);var c=JsonSerializer.SerializeToElement(beforeVsAfter);
                    check(stem+" product native edge matches both live WPF references",a.GetProperty("edgeMean").GetDouble()<2&&a.GetProperty("edgeChangedFraction").GetDouble()<.05&&b.GetProperty("edgeMean").GetDouble()<2&&b.GetProperty("edgeChangedFraction").GetDouble()<.05,new{nativeVsBefore,nativeVsAfter});
                    check(stem+" WPF edge is unchanged by native return",c.GetProperty("edgeMean").GetDouble()==0,beforeVsAfter);
                    if(scale==.82&&style=="matrix"&&alpha==1){var text=Field<TextBlock>("TimeLabel");check("product default compact layout retains Consolas Bold 14 DIP",text.FontFamily.Source=="Consolas"&&text.FontSize==14&&text.FontWeight==FontWeights.Bold,null);}
                }
                rows.Add(new{scale,style,alpha,hwnd=hwnd.ToInt64(),dpi,renderMode=actualRenderMode.ToString(),beforeShadowAlpha,nativeShadowAlpha,afterShadowAlpha=shadow.Opacity,nativeVsBefore,nativeVsAfter,beforeVsAfter});
            }
            finally
            {
                enabled.SetValue(window,true);typeof(IslandWindow).GetField("exiting",Flags)!.SetValue(window,true);window.Close();background?.Close();
                File.WriteAllText(Path.Combine(output,"native-raster-fresh.json"),JsonSerializer.Serialize(new{productDllSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),software,product,rows,scope="Each condition uses a fresh product IslandWindow HWND and isolated synthetic tracking data. Product mode keeps actual constructor backend; diagnostic modes explicitly override it. Real desktop captures compare WPF before first native enable, native steady frame and WPF return with native controller disabled. Product mode applies original strict edge thresholds to both live references. No real input/browser/game or physical DPI change."},AtomicStore.JsonOptions));
            }
        }
        // OS handles may be reused after a window closes; uniqueness is only
        // diagnostic, while every case owns a newly constructed Window/target.
        check("all 24 conditions used independently constructed windows",rows.Count==24,new{distinctObservedHandles=handles.Count,allHandlesUnique=allFresh});
    }
}
