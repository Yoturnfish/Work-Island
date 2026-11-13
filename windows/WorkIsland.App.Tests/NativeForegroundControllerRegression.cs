using System;
using System.Collections.Generic;
using System.Linq;
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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunNativeForegroundController(IslandWindow window,string output,Action<string,bool,object?> check,bool perfOnly=false)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;var progress=Field<PixelProgress>("GoalProgress");var border=Field<Border>("OuterBorder");
        bool Active()=>Field<bool>("nativeForegroundActive");int Captures()=>Field<int>("nativeForegroundCaptures");
        double Logical()=>(double)typeof(IslandWindow).GetProperty("PresentedWindowOpacity",Flags)!.GetValue(window)!;
        object? Pixel(string name)=>typeof(PixelProgress).GetField(name,Flags)!.GetValue(progress);
        object Native()=>Pixel("nativeScan")??throw new InvalidOperationException("Native scan missing: "+Field<string?>("nativeForegroundFailure"));
        double Alpha()=>Convert.ToDouble(Native().GetType().GetProperty("PresentationOpacity",Flags)!.GetValue(Native()));
        object Surface()=>Field<object>("nativeForegroundSurface");
        int SurfaceValue(string name)=>(int)Surface().GetType().GetProperty(name,Flags)!.GetValue(Surface())!;
        bool Near(double a,double b)=>Math.Abs(a-b)<.002;
        void State(string value){settings.Paused=value=="paused";engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new(value=="tracking"?"controller.exe":"other.exe","Controller fixture",0));Call("Render");}
        void Expand()=>Call("ToggleExpanded",null,new RoutedEventArgs());void Collapse()=>Call("CollapseToTimer");
        Field<DispatcherTimer>("fullscreenTimer").Stop();window.Left=800;window.Top=260;await Task.Delay(80);
        settings.HasCompletedOnboarding=true;settings.Rules=[new("controller.exe","Controller fixture")];settings.ReduceMotion=false;settings.BackgroundStyle="black";settings.BackgroundOpacity=.5;
        engine.Data.Sessions.Add(new(){ApplicationIdentifier="controller.exe",ApplicationName="Controller fixture",DayKey=LogicalDay.Key(DateTimeOffset.Now,engine.Zone,settings.DayResetMinutes),StartedAt=DateTimeOffset.Now.AddHours(-2),EndedAt=DateTimeOffset.Now,EffectiveSeconds=7200});
        Call("ApplyMaterial");Call("NotifyPointer",false);State("tracking");await Task.Delay(450);
        Call("EnableNativeForeground");await Task.Delay(100);
        check("controller enters grouped native foreground after tracking settles",Active()&&border.Opacity==0&&window.Opacity==1&&Near(Logical(),.5)&&Near(Alpha(),.5),new{active=Active(),border.Opacity,physicalOpacity=window.Opacity,logical=Logical(),alpha=Alpha(),window.IsMouseOver,failure=Field<string?>("nativeForegroundFailure")});
        if(!Active())return;
        var surfaces=Field<object>("surfaces");var shadow=(Window)surfaces.GetType().GetField("shadow",Flags)!.GetValue(surfaces)!;
        bool SurfaceVisible(string name)=>(bool)surfaces.GetType().GetProperty(name,Flags|BindingFlags.Public)!.GetValue(surfaces)!;
        check("native grouping preserves the original shadow opacity",Near(shadow.Opacity,.5),new{shadow.Opacity});
        var captures=Captures();for(var i=0;i<8;i++){Call("Render");await Task.Delay(20);}
        check("unchanged tracking renders do not upload a new foreground texture",Captures()==captures,new{before=captures,after=Captures()});
        Call("NotifyPointer",true);await Task.Delay(50);
        check("hover retains selected group and shadow opacity without recapturing",Active()&&Near(Alpha(),.5)&&Near(shadow.Opacity,.5)&&Captures()==captures,new{alpha=Alpha(),shadowOpacity=shadow.Opacity,captures=Captures()});
        Call("NotifyPointer",false);await Task.Delay(30);
        var record=engine.Data.Sessions[0];record.EffectiveSeconds+=60;Call("Render");await Task.Delay(80);
        check("changed visible minute uploads exactly one replacement texture",Active()&&Captures()==captures+1,new{before=captures,after=Captures(),label=Field<TextBlock>("TimeLabel").Text});
        State("away");await Task.Delay(70);
        var coffee=Field<FrameworkElement>("CoffeeIcon");var clockIcon=Field<FrameworkElement>("ClockIcon");
        check("state change keeps clock and coffee crossfade on the native plane",Active()&&Field<bool>("nativeStateTransitionActive")&&border.Opacity==0&&Near(Alpha(),.5)&&coffee.Opacity>0&&coffee.Opacity<1&&clockIcon.Opacity>0&&clockIcon.Opacity<1,new{active=Active(),coffee=coffee.Opacity,clock=clockIcon.Opacity});
        State("tracking");check("return state crossfade retains the current native plane",Active()&&Field<bool>("nativeStateTransitionActive"),null);await Task.Delay(450);
        check("settled tracking resumes native foreground automatically",Active()&&Near(Alpha(),.5),null);
        void KeyRoute(Key key,bool down)
        {
            var args=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),Environment.TickCount,key){RoutedEvent=down?Keyboard.PreviewKeyDownEvent:Keyboard.PreviewKeyUpEvent};
            window.RaiseEvent(args);
        }
        KeyRoute(Key.LeftCtrl,true);KeyRoute(Key.LeftCtrl,true);KeyRoute(Key.LeftShift,true);Call("Render");await Task.Delay(80);
        check("held and repeated keyboard input keeps ticking compact UI on live WPF",!Active()&&border.Opacity==1,null);
        KeyRoute(Key.LeftCtrl,false);Call("Render");await Task.Delay(80);
        check("releasing one of two held keys cannot restore a frozen native image",!Active(),null);
        KeyRoute(Key.LeftShift,false);await Task.Delay(80);
        check("releasing the last held key restores native foreground without a tick",Active(),null);
        KeyRoute(Key.LeftCtrl,true);Call("NativeForegroundDeactivated",null,EventArgs.Empty);await Task.Delay(80);
        check("deactivation clears a missing key-up and restores ready rendering",Active(),null);
        var snapshot=(InstanceSnapshot)typeof(IslandWindow).GetMethod("CaptureInstanceSnapshot",Flags)!.Invoke(window,null)!;
        check("runtime snapshot separates native physical and presented opacity",snapshot.Foreground is {Enabled:true,Active:true,Failed:false,HeldKeys:0}&&Near(snapshot.Foreground.PhysicalOpacity,1)&&Near(snapshot.Foreground.PresentedOpacity,.5)&&snapshot.Foreground.WindowHandle!=0,null);
        Expand();await Task.Delay(70);var compact=Field<FrameworkElement>("CompactPanel");var details=Field<FrameworkElement>("DetailsPanel");
        check("navigation uses live WPF during its original slide and fade",!Active()&&border.Opacity==1&&compact.Opacity>0&&compact.Opacity<1&&details.Opacity>0&&details.Opacity<1,new{compact.Opacity,incoming=details.Opacity});
        await Task.Delay(280);check("settled navigation keeps native foreground suspended",!Active()&&details.IsVisible&&!compact.IsVisible,null);
        Collapse();await Task.Delay(70);check("incoming compact page stays live during navigation",!Active()&&compact.Opacity>0&&compact.Opacity<1,null);await Task.Delay(300);
        check("navigation completion restores native foreground without a tracking tick",Active(),null);
        Expand();await Task.Delay(55);Collapse();await Task.Delay(320);check("rapid navigation reversal settles with grouped native foreground",Active()&&!Field<bool>("isExpanded")&&compact.Opacity==1,null);
        settings.ReduceMotion=true;Call("Render");await Task.Delay(60);check("reduced motion restores static WPF and stops both scan paths",!Active()&&!progress.ScanAnimating&&border.Opacity==1,null);
        settings.ReduceMotion=false;Call("Render");await Task.Delay(450);check("disabling reduced motion resumes grouped foreground",Active(),null);
        window.Hide();await Task.Delay(50);check("hiding stops native scan and all material windows",!Active()&&!progress.ScanAnimating&&!SurfaceVisible("ShadowVisible")&&!SurfaceVisible("BackdropVisible"),null);
        window.Show();Call("Render");await Task.Delay(450);check("showing resumes grouped foreground without re-enabling the feature",Active(),new{failure=Field<string?>("nativeForegroundFailure")});
        settings.IslandScale=.75;Call("ApplyScale");Call("Render");await Task.Delay(100);
        check("logical scaling resizes the existing native surface",Active()&&SurfaceValue("SurfaceResizes")>0,new{resizes=SurfaceValue("SurfaceResizes"),devices=SurfaceValue("DeviceCreates"),surfaces=SurfaceValue("SurfaceCreates")});
        settings.IslandScale=.82;Call("ApplyScale");Call("Render");await Task.Delay(100);
        check("right text retains approved font and size after native integration",Field<TextBlock>("TimeLabel").FontFamily.Source=="Consolas"&&Field<TextBlock>("TimeLabel").FontWeight==FontWeights.Bold&&Field<TextBlock>("TimeLabel").FontSize==14,null);
        var originalAlignment=Field<TextBlock>("TimeLabel").RenderTransform.Value;
        Field<TextBox>("TitleEditor").Visibility=Visibility.Visible;Field<TextBlock>("TitleLabel").Visibility=Visibility.Collapsed;Call("Render");await Task.Delay(50);
        check("title editing keeps live WPF instead of a frozen native image",!Active()&&border.Opacity==1,null);
        Call("CancelTitleEdit",null,null);await Task.Delay(80);
        check("leaving title editing restores native foreground with original text alignment",Active()&&Field<TextBlock>("TimeLabel").RenderTransform.Value==originalAlignment,null);
        Call("BeginOnboarding");await Task.Delay(70);
        check("onboarding entry uses live WPF and keeps native foreground hidden",!Active()&&border.Opacity==1&&Field<Grid>("onboardingRoot").Opacity>0,null);
        for(var i=0;i<4;i++)Call("MoveOnboarding",1);await Task.Delay(280);Call("FinishOnboarding");await Task.Delay(450);
        check("onboarding exit restores native foreground at completion without a timer tick",Active()&&Near(Logical(),1),new{active=Active(),logical=Logical()});
        Call("ReleaseOnboardingOpacityOutside",new Point(-1,-1));Call("NotifyPointer",false);await Task.Delay(50);
        var input=Field<FrameworkElement>("ExpandButton");var center=input.PointToScreen(new Point(input.ActualWidth/2,input.ActualHeight/2));var ownHandle=new WindowInteropHelper(window).Handle;
        check("integrated input stencil retains the original Windows mouse target",ControllerWindowFromPoint(new((int)Math.Round(center.X),(int)Math.Round(center.Y)))==ownHandle,new{ownHandle=ownHandle.ToInt64(),scope="Read-only WindowFromPoint. Does not count as actual mouse acceptance."});
        var cpu=new List<object>();var cpuFocus=new Window{Title="Work Island owned CPU focus guard",ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None,Width=64,Height=64,Left=800,Top=600};
        var objectQueries=0;var ownSource=HwndSource.FromHwnd(ownHandle)!;
        nint CpuMessages(nint hwnd,int message,nint w,nint l,ref bool handled){if(message==0x003D)objectQueries++;return 0;}
        cpuFocus.Show();cpuFocus.Activate();ownSource.AddHook(CpuMessages);
        try
        {
            using var process=Process.GetCurrentProcess();await Task.Delay(700);
            foreach(var nativeMode in new[]{false,true})
            {
                if(nativeMode)Call("QueueNativeForeground");else Call("SuspendNativeForeground");await Task.Delay(100);
                var audit=JsonSerializer.Serialize(AuditControllerClocks(window),AtomicStore.JsonOptions);
                File.WriteAllText(Path.Combine(output,nativeMode?"native-clock-audit.json":"wpf-clock-audit.json"),audit);
                if(nativeMode)
                {
                    using var parsed=JsonDocument.Parse(audit);var root=parsed.RootElement;
                    var forever=root.GetProperty("clocks").EnumerateArray().Count(c=>c.GetProperty("state").GetString()=="Active"&&c.GetProperty("repeat").GetString()=="Forever");
                    check("steady native tracking leaves no running WPF forever clock after state and navigation changes",root.GetProperty("failure").ValueKind==JsonValueKind.Null&&forever==0,new{forever});
                }
                var beforeCaptures=Captures();
                for(var round=0;round<2;round++)
                {
                    Dictionary<int,double> Threads(){var values=new Dictionary<int,double>();foreach(ProcessThread thread in process.Threads){using(thread)try{values[thread.Id]=thread.TotalProcessorTime.TotalSeconds;}catch(Exception error)when(error is InvalidOperationException or System.ComponentModel.Win32Exception){}}return values;}
                    process.Refresh();var beforeThreads=Threads();objectQueries=0;var before=process.TotalProcessorTime.TotalSeconds;var watch=Stopwatch.StartNew();
                    for(var second=0;second<4;second++){Call("Render");if(!nativeMode)Call("SuspendNativeForeground");await Task.Delay(1000);}
                    process.Refresh();var elapsed=watch.Elapsed.TotalSeconds;var used=process.TotalProcessorTime.TotalSeconds-before;var afterThreads=Threads();
                    cpu.Add(new{nativeMode,round,seconds=elapsed,oneCorePercent=100*used/elapsed,active=Active(),isForeground=window.IsActive,objectQueries,captures=Captures()-beforeCaptures,wpfScan=((TranslateTransform)Pixel("scan")!).HasAnimatedProperties,wpfPulse=progress.PulseAnimating,statusScale=Field<FrameworkElement>("StatusIcon").RenderTransform.HasAnimatedProperties,transition=Field<object?>("stateTransition")!=null,statusTimer=Field<DispatcherTimer>("statusAnimationTimer").IsEnabled,threads=afterThreads.Select(pair=>new{id=pair.Key,ui=pair.Key==AuditGetCurrentThreadId(),cpuSeconds=pair.Value-(beforeThreads.TryGetValue(pair.Key,out var old)?old:pair.Value)}).Where(t=>t.cpuSeconds>.01).OrderByDescending(t=>t.cpuSeconds).ToArray()});
                }
            }
        }
        finally{ownSource.RemoveHook(CpuMessages);cpuFocus.Close();}
        File.WriteAllText(Path.Combine(output,"controller-cpu.json"),JsonSerializer.Serialize(new{samples=cpu,scope="Same isolated actual IslandWindow and owned focus guard for both controller paths, two 4-second rounds each, production Render once per second in both paths, no screenshots or forced GC. WPF is the controller fallback path, not an old sealed binary. Records incoming WM_GETOBJECT queries. Excludes cold construction, WindowsActivity recognition, DWM/GPU and long-run acceptance; foreground-query cost is separately retained in native-quiet audit."},AtomicStore.JsonOptions));
        if(perfOnly)return;

        var background=new Window{Title="Work Island controller capture guard",ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Left=window.Left-100,Top=window.Top-100,Width=window.Width+200,Height=window.Height+200,Background=Brushes.White};
        var backgroundField=typeof(IslandWindow).GetField("fidelityBackground",Flags)!;background.Show();backgroundField.SetValue(window,background);
        try
        {
            var pixelComparisons=new List<object>();
            foreach(var style in new[]{"matrix","waveform"})foreach(var opacity in new[]{1d,.8,.5,.25})
            {
                settings.ProgressStyle=style;settings.BackgroundOpacity=opacity;Call("ApplyMaterial");Call("NotifyPointer",false);Call("Render");await Task.Delay(90);
                Native().GetType().GetMethod("FreezePhaseForReview",Flags)!.Invoke(Native(),[.32f]);await Task.Delay(120);
                var saveStatic=typeof(IslandWindow).GetMethod("SaveScreenRegion",Flags)!;var prefix=$"controller-{style}-{opacity:0.00}";var nativePath=Path.Combine(output,prefix+"-native.png");saveStatic.Invoke(window,[nativePath,23]);
                var capturedAlpha=Alpha();var capturedPhysical=window.Opacity;Call("SuspendNativeForeground");
                var translate=(TranslateTransform)Pixel("scan")!;var band=(System.Windows.Shapes.Rectangle)Pixel("filledBand")!;translate.BeginAnimation(TranslateTransform.XProperty,null);translate.X=-band.Width+(progress.ActualWidth+band.Width)*.32;
                await Task.Delay(120);var wpfPath=Path.Combine(output,prefix+"-wpf.png");saveStatic.Invoke(window,[wpfPath,23]);
                using var actual=new System.Drawing.Bitmap(nativePath);using var reference=new System.Drawing.Bitmap(wpfPath);
                var dpi=VisualTreeHelper.GetDpi(window);var origin=window.PointToScreen(new Point());var progressOrigin=progress.PointToScreen(new Point());
                var roi=new Rect(Math.Ceiling(23*dpi.DpiScaleX)+progressOrigin.X-origin.X,Math.Ceiling(23*dpi.DpiScaleY)+progressOrigin.Y-origin.Y,progress.ActualWidth*dpi.DpiScaleX,progress.ActualHeight*dpi.DpiScaleY);
                double allSum=0,roiSum=0;var count=0;var changed=0;
                for(var y=0;y<actual.Height;y++)for(var x=0;x<actual.Width;x++)
                {
                    var a=actual.GetPixel(x,y);var b=reference.GetPixel(x,y);var red=Math.Abs(a.R-b.R);var green=Math.Abs(a.G-b.G);var blue=Math.Abs(a.B-b.B);allSum+=red+green+blue;
                    if(roi.Contains(x,y)){roiSum+=red+green+blue;count++;if(Math.Max(red,Math.Max(green,blue))>12)changed++;}
                }
                var mean=allSum/(actual.Width*actual.Height*3);var progressMean=roiSum/(count*3);var changedFraction=(double)changed/count;
                check($"integrated {style} foreground matches live WPF at alpha {opacity}",Near(capturedAlpha,opacity)&&capturedPhysical==1&&Near(window.Opacity,opacity)&&progressMean<1&&changedFraction<.02,new{mean,progressMean,changedFraction,capturedAlpha,capturedPhysical,referenceAlpha=window.Opacity});
                pixelComparisons.Add(new{style,opacity,mean,progressMean,changedFraction,nativePath,wpfPath});
            }
            File.WriteAllText(Path.Combine(output,"controller-static-pixels.json"),JsonSerializer.Serialize(new{pixelComparisons,scope="Integrated production controller versus live WPF at the same fixed phase, actual screen over owned white background. Includes input stencil; fixed progress ROI gates unchanged from prior group experiment."},AtomicStore.JsonOptions));
            settings.ProgressStyle="matrix";settings.BackgroundOpacity=.5;Call("ApplyMaterial");Call("NotifyPointer",false);Call("Render");await Task.Delay(100);
            var source=Field<FullscreenFixture>("fullscreenSource");
            var events=new List<object>();var samples=new List<object>();var motion=Path.Combine(output,"motion");var frames=Path.Combine(motion,"frames");Directory.CreateDirectory(frames);
            var save=typeof(IslandWindow).GetMethod("SaveScreenRegion",Flags)!;var watch=Stopwatch.StartNew();var step=0;
            (double At,string Name,Action Action)[] actions=[
                (.5,"hover",()=>Call("NotifyPointer",true)),(1,"leave",()=>Call("NotifyPointer",false)),
                (1.5,"away",()=>State("away")),(2.2,"tracking",()=>State("tracking")),
                (3,"expand",Expand),(3.5,"collapse",Collapse),(4.2,"expand",Expand),(4.28,"reverse",Collapse),
                (5,"pause",()=>State("paused")),(5.08,"reverse-tracking",()=>State("tracking")),
                (6,"glass",()=>{settings.BackgroundStyle="glass";Call("ApplyMaterial");Call("Render");}),
                (7,"black",()=>{settings.BackgroundStyle="black";Call("ApplyMaterial");Call("Render");}),
                (8,"hide",()=>window.Hide()),(8.4,"show",()=>{window.Show();Call("Render");}),
                (9,"fullscreen-fixture",()=>{source.Active=true;Call("RefreshFullscreenAvoidance");}),
                (9.5,"fullscreen-clear-start",()=>{source.Active=false;Call("RefreshFullscreenAvoidance");}),
                (10.6,"fullscreen-clear-settle",()=>Call("RefreshFullscreenAvoidance"))];
            while(watch.Elapsed.TotalSeconds<12)
            {
                while(step<actions.Length&&watch.Elapsed.TotalSeconds>=actions[step].At)
                {
                    var action=actions[step++];action.Action();events.Add(new{at=watch.Elapsed.TotalSeconds,name=action.Name,active=Active(),visible=window.IsVisible,logical=Logical(),physical=window.Opacity,captures=Captures()});
                }
                var at=watch.Elapsed.TotalSeconds;var file=$"frame-{samples.Count:00000}.png";save.Invoke(window,[Path.Combine(frames,file),23]);samples.Add(new{file,elapsedSeconds=at,active=Active(),visible=window.IsVisible,logical=Logical(),physical=window.Opacity});await Task.Delay(20);
            }
            File.WriteAllText(Path.Combine(motion,"motion-capture.json"),JsonSerializer.Serialize(new{durationSeconds=watch.Elapsed.TotalSeconds,frameCount=samples.Count,frames=samples,events,scope="Actual screen capture of isolated IslandWindow with production controller. Programmatic real state/navigation/material handlers and simulated fullscreen source; no real game, mouse input, physical DPI or preview acceptance."},AtomicStore.JsonOptions));
            check("fullscreen fixture restores native foreground after the existing clear delay",Active()&&window.IsVisible&&!source.Active,new{active=Active(),visible=window.IsVisible});
        }
        finally{backgroundField.SetValue(window,null);background.Close();}
        var ownedSurface=Surface();((IDisposable)ownedSurface).Dispose();record.EffectiveSeconds+=60;Call("Render");await Task.Delay(100);
        check("native upload failure restores usable WPF without retrying a broken surface",Field<bool>("nativeForegroundFailed")&&!Active()&&border.Opacity==1&&Near(window.Opacity,.5)&&progress.ScanAnimating,new{failure=Field<string?>("nativeForegroundFailure"),active=Active(),borderOpacity=border.Opacity,windowOpacity=window.Opacity});
        Call("DisposeNativeForeground");
        check("controller disposal releases its input adorner and surface ownership",!Field<bool>("nativeForegroundEnabled")&&Field<object?>("nativeForegroundInput")==null&&Field<object?>("nativeForegroundSurface")==null,null);
        var closeState=JsonSerializer.Deserialize<AppState>(JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions),AtomicStore.JsonOptions)!;closeState.Settings.BackgroundStyle="black";closeState.Settings.BackgroundOpacity=.5;closeState.Settings.WindowX=320;closeState.Settings.WindowY=260;
        var closeWindow=new IslandWindow(new AtomicStore(Path.Combine(output,"close-state.json")),closeState,false,null,startupRegistration:new IsolatedStartupRegistration(Path.Combine(output,"close-startup.json"),"close-fixture"),recognitionSource:new RecognitionFixture(),applicationPicker:new ApplicationPickerFixture(),fullscreenSource:new FullscreenFixture());
        try
        {
            closeWindow.ShowActivated=false;closeWindow.Topmost=false;closeWindow.Loaded+=(_,_)=>((DispatcherTimer)typeof(IslandWindow).GetField("timer",Flags)!.GetValue(closeWindow)!).Stop();closeWindow.Show();
            ((DispatcherTimer)typeof(IslandWindow).GetField("fullscreenTimer",Flags)!.GetValue(closeWindow)!).Stop();
            var closeEngine=(TrackingEngine)typeof(IslandWindow).GetField("engine",Flags)!.GetValue(closeWindow)!;closeEngine.ResetObservation();closeEngine.Observe(DateTimeOffset.Now,new("controller.exe","Close fixture",0));typeof(IslandWindow).GetMethod("Render",Flags|BindingFlags.DeclaredOnly)!.Invoke(closeWindow,null);await Task.Delay(450);
            typeof(IslandWindow).GetMethod("EnableNativeForeground",Flags)!.Invoke(closeWindow,null);await Task.Delay(100);
            var closeActive=(bool)typeof(IslandWindow).GetField("nativeForegroundActive",Flags)!.GetValue(closeWindow)!;
            var closeSurface=typeof(IslandWindow).GetField("nativeForegroundSurface",Flags)!.GetValue(closeWindow)!;
            var closeProgress=(PixelProgress)typeof(IslandWindow).GetField("GoalProgress",Flags)!.GetValue(closeWindow)!;var closeNative=typeof(PixelProgress).GetField("nativeScan",Flags)!.GetValue(closeProgress)!;
            typeof(IslandWindow).GetField("exiting",Flags)!.SetValue(closeWindow,true);closeWindow.Close();await Task.Delay(80);
            var remaining=(int)closeSurface.GetType().GetProperty("OwnedReferences",Flags)!.GetValue(closeSurface)!;
            var nativeRemaining=((System.Collections.ICollection)closeNative.GetType().GetField("references",Flags)!.GetValue(closeNative)!).Count;
            check("closing an active native window releases surface and compositor ownership",closeActive&&remaining==0&&nativeRemaining==0&&!closeWindow.IsVisible,new{closeActive,remaining,nativeRemaining});
        }
        finally{typeof(IslandWindow).GetField("exiting",Flags)!.SetValue(closeWindow,true);if(closeWindow.IsVisible)closeWindow.Close();}
        File.WriteAllText(Path.Combine(output,"native-controller.json"),JsonSerializer.Serialize(new{productDllSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),completed=true,captures=Captures(),failureProbe=Field<string?>("nativeForegroundFailure"),scope="Explicitly enabled actual production controller in isolated IslandWindow. Normal preview startup remains unchanged. Failure case deliberately disposes the owned upload surface; this is not a physical GPU device-loss test."},AtomicStore.JsonOptions));
    }
    [StructLayout(LayoutKind.Sequential)]private readonly record struct ControllerPoint(int X,int Y);
    [DllImport("user32.dll",EntryPoint="WindowFromPoint")]private static extern nint ControllerWindowFromPoint(ControllerPoint point);
}
