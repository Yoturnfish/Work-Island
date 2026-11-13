using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunProfilePreviewMotion(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        object? Pixel(PixelProgress p,string name)=>typeof(PixelProgress).GetField(name,Flags)!.GetValue(p);
        AnimationClock? Clock(PixelProgress p)=>(AnimationClock?)Pixel(p,"scanClock");
        object? Native(PixelProgress p)=>Pixel(p,"nativeScan");
        object? Renderer(PixelProgress p)=>Native(p)??(object?)Clock(p);
        double X(PixelProgress p)
        {
            if(Native(p) is not {} native)return ((TranslateTransform)Pixel(p,"scan")!).X;
            var type=native.GetType();var width=(double)type.GetField("width",Flags)!.GetValue(native)!;var band=(double)type.GetField("band",Flags)!.GetValue(native)!;
            return -band+(width+band)*(double)type.GetProperty("EstimatedPhase",Flags)!.GetValue(native)!;
        }
        bool SourceDuration(PixelProgress p)=>Native(p) is {} native
            ?((TimeSpan)native.GetType().GetProperty("ScanDuration",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!).TotalSeconds==2.8
            :Clock(p)?.Timeline is DoubleAnimation a&&a.Duration.TimeSpan.TotalSeconds==2.8&&a.EasingFunction is null;
        bool Released(PixelProgress p)=>!p.IsLoaded&&!p.ScanAnimating&&Clock(p) is null&&!(bool)Pixel(p,"accentObserved")!;
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;settings.Paused=true;settings.Rules=[];settings.ReduceMotion=false;settings.BackgroundStyle="black";
        settings.HasCompletedOnboarding=true;engine.ResetObservation();Field<DispatcherTimer>("fullscreenTimer").Stop();Field<DispatcherTimer>("focusReturn").Stop();
        window.Left=460;window.Top=300;window.Topmost=true;window.Title="Work Island · 样式预览动效验收";
        var background=new Window{Title="Work Island · 自有预览背景",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Left=300,Top=180,Width=1100,Height=650,Background=new SolidColorBrush(Color.FromRgb(19,24,20)),ShowActivated=false,ShowInTaskbar=false};
        background.Show();window.Activate();Call("Persist");Call("Render");Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenRecognition",window,new RoutedEventArgs());await Task.Delay(300);
        if(!SystemParameters.ClientAreaAnimation)throw new NotSupportedException("System animation is disabled; actual enabled-motion acceptance needs the user's policy change. No system setting is modified by this fixture.");
        var color=Field<Button>("RecognitionColorButton");ContextMenu menu=null!;StackPanel panel=null!;PixelProgress[] previews=[];
        void ReadMenu(){menu=color.ContextMenu!;panel=(StackPanel)((MenuItem)menu.Items[0]).Header;previews=panel.Children.OfType<StackPanel>().Single().Children.OfType<Button>().Select(b=>((Grid)b.Content).Children.OfType<PixelProgress>().Single()).ToArray();}
        void Open(){Call("OpenProfilePopover");ReadMenu();Field<DispatcherTimer>("focusReturn").Stop();}
        void ColorAt(int index)=>panel.Children.OfType<UniformGrid>().Single().Children.OfType<StackPanel>().ElementAt(index).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void StyleAt(int index)=>panel.Children.OfType<StackPanel>().Single().Children.OfType<Button>().ElementAt(index).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var history=JsonSerializer.Serialize(engine.Data.Sessions,AtomicStore.JsonOptions);var cost=new List<object>();
        async Task Measure(string phase)
        {
            await Task.Delay(200);using var p=Process.GetCurrentProcess();var cpu=p.TotalProcessorTime.TotalSeconds;var watch=Stopwatch.StartNew();await Task.Delay(4000);p.Refresh();
            var delta=p.TotalProcessorTime.TotalSeconds-cpu;
            cost.Add(new{phase,seconds=watch.Elapsed.TotalSeconds,cpuSeconds=delta,oneCorePercent=100*delta/watch.Elapsed.TotalSeconds,machinePercent=100*delta/watch.Elapsed.TotalSeconds/Environment.ProcessorCount,logicalProcessors=Environment.ProcessorCount,privateBytes=p.PrivateMemorySize64,handles=p.HandleCount,scan=previews.Select(p=>p.ScanAnimating).ToArray(),native=previews.Select(p=>Native(p) is not null).ToArray(),wpfClock=previews.Select(p=>Clock(p) is not null).ToArray(),scope="Private WPF fixture, main sampling timer stopped, no Computer Use session or frame capture during this sample; bounded comparison, not ordinary preview or long-run cost."});
        }
        var motion=Path.Combine(output,"motion");Directory.CreateDirectory(Path.Combine(motion,"frames"));var frames=new List<object>();var events=new List<object>();var oldPreviews=new List<PixelProgress>();var watch=new Stopwatch();
        var retainedBrushes=new List<nint>();nint scanProperty=0;
        bool HasController(nint observer)
        {
            var get=Marshal.GetDelegateForFunctionPointer<DisposalController>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(observer),6*nint.Size));
            Marshal.ThrowExceptionForHR(get(observer,scanProperty,out var controller));if(controller==0)return false;Marshal.Release(controller);return true;
        }
        try
        {
            Open();await Task.Delay(150);
            check("both visible style previews scan even while main timer is paused",settings.Paused&&previews.All(p=>p.IsLoaded&&p.ScanAnimating),null);
            check("both preview renderers use Mac linear 2.8-second scan",previews.All(SourceDuration),null);
            check("popup previews use native scans without WPF animation clocks",previews.All(p=>Native(p) is not null&&Clock(p) is null&&p.ScanAnimating),previews.Select(p=>new{native=Native(p) is not null,wpf=Clock(p) is not null,failure=Pixel(p,"nativeFailure")}).ToArray());
            check("two native previews share one popup target",previews.Select(Native).Where(n=>n is not null).Count(n=>(nint)n!.GetType().GetField("target",Flags)!.GetValue(n)! !=0)==1&&previews.Select(Native).Where(n=>n is not null).Count(n=>(nint)n!.GetType().GetField("parentChildren",Flags)!.GetValue(n)! !=0)==1,null);
            settings.ReduceMotion=true;Call("Render");await Task.Delay(80);
            check("open preview responds to reduced-motion policy without rebuilding the menu",menu.IsOpen&&previews.All(p=>!p.ScanAnimating&&Clock(p) is null),null);
            await Measure("open-static");settings.ReduceMotion=false;Call("Render");await Task.Delay(80);
            check("open preview resumes when animation is allowed again",previews.All(p=>p.ScanAnimating),null);
            await Measure("open-animated");
            settings.ReduceMotion=true;Call("Render");await Measure("open-static-return");
            settings.ReduceMotion=false;Call("Render");await Measure("open-animated-return");
            menu.IsOpen=false;await Task.Delay(300);await Measure("closed-after-animation");Open();await Task.Delay(150);
            var originalClocks=previews.Select(Renderer).ToArray();var originalEpoch=previews.Select(p=>(long)Pixel(p,"scanEpoch")!).ToArray();
            if(previews.All(p=>Native(p) is not null))
            {
                Marshal.ThrowExceptionForHR(DisposalCreateString("Offset.X",8,out scanProperty));
                foreach(var p in previews)
                {
                    var native=Native(p)!;var brushes=(List<nint>)native.GetType().GetField("brushes",Flags)!.GetValue(native)!;var iid=new Guid("0bb3784c-346b-4a7c-966b-7310966553d5");
                    Marshal.ThrowExceptionForHR(Marshal.QueryInterface(brushes[0],in iid,out var observer));retainedBrushes.Add(observer);
                }
            }
            check("both previews have real compositor animation controllers",retainedBrushes.Count==2&&retainedBrushes.All(HasController),new{retained=retainedBrushes.Count});
            StyleAt(1);await Task.Delay(70);
            check("style selection keeps the existing scan renderers and phase epochs",previews.Select(Renderer).SequenceEqual(originalClocks)&&previews.Select(p=>(long)Pixel(p,"scanEpoch")!).SequenceEqual(originalEpoch)&&menu.IsOpen,null);
            ColorAt(1);await Task.Delay(70);
            check("color selection keeps both scan phases while updating preview ink",previews.Select(Renderer).SequenceEqual(originalClocks)&&previews.Select(p=>(long)Pixel(p,"scanEpoch")!).SequenceEqual(originalEpoch)&&previews.All(p=>((SolidColorBrush)Pixel(p,"accent")!).Color==Color.FromRgb(51,140,255)),null);
            settings.ReduceMotion=true;Call("Render");settings.ReduceMotion=false;Call("Render");await Task.Delay(100);
            check("rapid reduced-motion reversal leaves one live scan renderer per preview",previews.All(p=>p.ScanAnimating&&Renderer(p) is not null&&Pixel(p,"pulseClock") is null&&(Native(p) is null||Clock(p) is null)),null);
            var capturedReferences=previews.ToArray();var first=previews.Select(p=>X(p)).ToArray();var positions=new List<double[]>();var phase=0;var coverFailures=0;
            WindowsActivity.GetWindowRect(((HwndSource)PresentationSource.FromVisual(menu)).Handle,out var rect);
            object Bounds(PixelProgress p){var a=p.PointToScreen(new Point());var b=p.PointToScreen(new Point(p.ActualWidth,p.ActualHeight));return new{x=a.X-rect.Left,y=a.Y-rect.Top,width=b.X-a.X,height=b.Y-a.Y};}
            var rois=previews.Select(Bounds).ToArray();watch.Start();
            while(watch.Elapsed.TotalSeconds<9)
            {
                var at=watch.Elapsed.TotalSeconds;
                if(phase==0&&at>=2){StyleAt(0);events.Add(new{name="style",at});phase++;}
                if(phase==1&&at>=3){ColorAt(0);events.Add(new{name="color",at});phase++;}
                if(phase==2&&at>=4.8){settings.ReduceMotion=true;Call("Render");events.Add(new{name="reduced",at});phase++;}
                if(phase==3&&at>=5.8){settings.ReduceMotion=false;Call("Render");events.Add(new{name="resume",at});phase++;}
                if(phase==4&&at>=6.6){oldPreviews.AddRange(previews);Call("HideIsland",null,new RoutedEventArgs());events.Add(new{name="hide",at});phase++;}
                if(phase==5&&at>=7.1){Call("ShowIsland");Call("OpenRecognition",window,new RoutedEventArgs());Open();events.Add(new{name="reopen",at});phase++;}
                if(phase==6&&at>=8.4){oldPreviews.AddRange(previews);menu.IsOpen=false;events.Add(new{name="close",at});phase++;}
                if(menu.IsOpen&&PresentationSource.FromVisual(menu) is HwndSource source)WindowsActivity.GetWindowRect(source.Handle,out rect);
                WindowsActivity.GetWindowRect(new WindowInteropHelper(background).Handle,out var bg);
                if(rect.Left<bg.Left||rect.Top<bg.Top||rect.Right>bg.Right||rect.Bottom>bg.Bottom)coverFailures++;
                var file=$"{frames.Count:D5}.png";
                using(var bitmap=new System.Drawing.Bitmap(rect.Right-rect.Left,rect.Bottom-rect.Top))
                {
                    using(var graphics=System.Drawing.Graphics.FromImage(bitmap))graphics.CopyFromScreen(rect.Left,rect.Top,0,0,bitmap.Size);
                    bitmap.Save(Path.Combine(motion,"frames",file),System.Drawing.Imaging.ImageFormat.Png);
                }
                var xs=previews.Select(X).ToArray();if(at<4.5)positions.Add(xs);
                frames.Add(new{file,elapsedSeconds=at,captureEndSeconds=watch.Elapsed.TotalSeconds,xs,scan=previews.Select(p=>p.ScanAnimating).ToArray(),native=previews.Select(p=>Native(p) is not null).ToArray(),wpfClock=previews.Select(p=>Clock(p) is not null).ToArray(),clockSeconds=previews.Select(p=>Clock(p)?.CurrentTime?.TotalSeconds).ToArray(),reduced=settings.ReduceMotion,menuOpen=menu.IsOpen});
                await Task.Delay(20);
            }
            watch.Stop(); // Later fallback checks are outside the recorded movie.
            check("captured live preview includes moving intermediate scan positions",positions.Count>40&&Enumerable.Range(0,2).All(i=>positions.Max(x=>x[i])-positions.Min(x=>x[i])>100),new{samples=positions.Count});
            check("recording covers both style and color updates, reduction, hide and reopen",phase==7&&frames.Count>140,new{phase,count=frames.Count});
            check("captured popup rectangles stay over owned background only",coverFailures==0,new{coverFailures});
            await Task.Delay(220);
            check("hidden and closed animated previews release clocks and brush observations",oldPreviews.Count==4&&oldPreviews.All(Released),null);
            check("closed shared-host previews stop externally retained brush controllers",retainedBrushes.Count==2&&retainedBrushes.All(p=>!HasController(p)),null);
            check("closing the current popup releases its render-policy callback",typeof(IslandWindow).GetField("profilePreviewMotion",Flags)?.GetValue(window) is null,null);
            check("preview animation leaves user history and main pause unchanged",settings.Paused&&engine.Current.State=="paused"&&JsonSerializer.Serialize(engine.Data.Sessions,AtomicStore.JsonOptions)==history,null);
            Open();await Task.Delay(120);
            if(Native(previews[0]) is not null)
                typeof(PixelProgress).GetMethod("UseWpfScan",Flags)!.Invoke(previews[0],[new NotSupportedException("Own controlled popup-target failure fixture")]);
            await Task.Delay(100);
            check("a controlled shared-target failure keeps both previews animating through WPF",menu.IsOpen&&previews.All(p=>Native(p) is null&&Clock(p) is not null&&p.ScanAnimating),null);
            settings.ReduceMotion=true;Call("Render");await Task.Delay(50);
            check("shared-target fallback still obeys reduced motion",previews.All(p=>!p.ScanAnimating&&Clock(p) is null),null);
            var fallback=previews.ToArray();menu.IsOpen=false;await Task.Delay(220);
            check("closing a failed native target releases both fallback previews",fallback.All(Released),null);
            File.WriteAllText(Path.Combine(motion,"motion-capture.json"),JsonSerializer.Serialize(new{durationSeconds=watch.Elapsed.TotalSeconds,frames,events,rois,scope="Actual own popup desktop pixels over owned solid background; wall-clock capture times, controlled production settings/actions. Native X values are monotonic phase estimates; actual moving pixels are independently analyzed. Not native input, Mac physical video, constant frame rate or long-run acceptance."},AtomicStore.JsonOptions));
            File.WriteAllText(Path.Combine(output,"preview-cost.json"),JsonSerializer.Serialize(new{cost,scope="Two bounded phases in one private validator process; screenshot capture excluded. No global resource-fix claim."},AtomicStore.JsonOptions));
        }
        finally
        {
            foreach(var brush in retainedBrushes)Marshal.Release(brush);if(scanProperty!=0)DisposalDeleteString(scanProperty);
            if(color.ContextMenu is {} current)current.IsOpen=false;
            await Task.Delay(300);background.Close();Call("ShowPage","home",false);Call("CollapseToTimer");Field<DispatcherTimer>("focusReturn").Stop();
        }
    }
}
