using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunSystemMaterialPolicyChecks(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;settings.HasCompletedOnboarding=true;settings.BackgroundStyle="glass";settings.Rules=[new("policy-fixture.exe","Policy fixture")];settings.ReduceMotion=false;settings.Paused=false;
        Field<DispatcherTimer>("fullscreenTimer").Stop();window.Left=320;window.Top=260;window.Topmost=true;
        var surfaceType=typeof(IslandWindow).Assembly.GetType("WorkIsland.App.IslandSurfaces")!;var surfaceField=typeof(IslandWindow).GetField("surfaces",Flags)!;
        var oldSurfaces=surfaceField.GetValue(window)!;var originalMonitor=surfaceType.GetField("effectsMonitor",Flags)!.GetValue(oldSurfaces);
        check("ordinary material surface subscribes documented UISettings effect event",(bool)surfaceType.GetProperty("SystemEffectsSubscribed")!.GetValue(oldSurfaces)!,new{result=surfaceType.GetProperty("SystemEffectsWatchResult")!.GetValue(oldSurfaces)});
        ((IDisposable)oldSurfaces).Dispose();
        if(originalMonitor is not null)check("replacing material surface unsubscribes original real event",!(bool)originalMonitor.GetType().GetProperty("Subscribed",Flags)!.GetValue(originalMonitor)!&&(int)originalMonitor.GetType().GetProperty("OwnedReferences",Flags)!.GetValue(originalMonitor)! == 0,null);
        var monitorType=typeof(IslandWindow).Assembly.GetType("WorkIsland.App.SystemEffectsMonitor")!;var callbackCount=0;var callbackThread=0;Action callback=()=>{System.Threading.Interlocked.Increment(ref callbackCount);callbackThread=Environment.CurrentManagedThreadId;};
        var monitor=monitorType.GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,[typeof(Action)],null)!.Invoke([callback]);var handler=(nint)monitorType.GetField("handler",Flags)!.GetValue(monitor)!;Marshal.AddRef(handler);
        try
        {
            var invoke=Marshal.GetDelegateForFunctionPointer<PolicyInvoke>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(handler),3*IntPtr.Size));var uiThread=Environment.CurrentManagedThreadId;
            var result=await Task.Run(()=>invoke(handler,0,0));check("registered WinRT delegate ABI invokes safely from a worker thread",result==0&&callbackCount==1&&callbackThread!=uiThread,new{result,callbackCount,callbackThread,uiThread});
            ((IDisposable)monitor).Dispose();result=await Task.Run(()=>invoke(handler,0,0));
            check("event removal releases monitor references and detaches late callbacks",result==0&&callbackCount==1&&(int)monitorType.GetProperty("OwnedReferences",Flags)!.GetValue(monitor)! == 0&&(int)monitorType.GetProperty("RemoveResult",Flags)!.GetValue(monitor)! == 0,null);
        }
        finally{((IDisposable)monitor).Dispose();Marshal.Release(handler);}
        var allowed=true;var unreadable=false;var reads=0;Func<bool> policy=()=>{reads++;if(unreadable)throw new UnauthorizedAccessException("isolated policy provider");return allowed;};
        var ctor=surfaceType.GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,[typeof(IslandWindow),typeof(Func<bool>)],null)!;var surfaces=ctor.Invoke([window,policy]);surfaceField.SetValue(window,surfaces);
        object? SurfaceField(string name)=>surfaceType.GetField(name,Flags)!.GetValue(surfaces);
        bool Available()=>(bool)surfaceType.GetProperty("NativeBackdropAvailable")!.GetValue(surfaces)!;
        bool BackdropVisible()=>(bool)surfaceType.GetProperty("BackdropVisible")!.GetValue(surfaces)!;
        bool ShadowVisible()=>(bool)surfaceType.GetProperty("ShadowVisible")!.GetValue(surfaces)!;
        int References(object? host)=>host is null?0:((System.Collections.ICollection)host.GetType().GetField("references",Flags)!.GetValue(host)!).Count;
        float Blend()
        {
            var host=SurfaceField("nativeBackdrop")!;var visual=(nint)host.GetType().GetField("visual",Flags)!.GetValue(host)!;
            var getter=Marshal.GetDelegateForFunctionPointer<PolicyGetFloat>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(visual),22*IntPtr.Size));Marshal.ThrowExceptionForHR(getter(visual,out var value));return value;
        }
        Color BackgroundColor()=>((LinearGradientBrush)Field<Border>("OuterBorder").Background).GradientStops[0].Color;
        void Notification(int message)=>surfaceType.GetMethod("ForegroundChanged",Flags)!.Invoke(surfaces,[new WindowInteropHelper(window).Handle,message,(nint)0,(nint)0,false]);
        var rows=new List<object>();void Sample(string phase)=>rows.Add(new{phase,reads,allowed,available=Available(),backdrop=BackdropVisible(),background=BackgroundColor().ToString(),nativeActive=Field<bool>("nativeForegroundActive")});
        var background=new Window{Title="Work Island isolated system material reference",ShowActivated=false,ShowInTaskbar=false,Topmost=true,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Background=Brushes.White,Left=250,Top=190,Width=620,Height=300};background.Show();typeof(IslandWindow).GetField("fidelityBackground",Flags)!.SetValue(window,background);
        var save=typeof(IslandWindow).GetMethod("SaveScreenRegion",Flags)!;
        try
        {
            var now=DateTimeOffset.Now;engine.ResetObservation();engine.Observe(now,new("policy-fixture.exe","Policy fixture",0));Call("ApplyMaterial");Call("NotifyPointer",false);Call("Render");await Task.Delay(450);Call("EnableNativeForeground");await Task.Delay(150);
            var original=SurfaceField("nativeBackdrop");Sample("enabled");save.Invoke(window,[Path.Combine(output,"01-enabled.png"),23]);check("initial allowed policy creates actual native glass",Available()&&BackdropVisible()&&original is not null&&Field<bool>("nativeForegroundActive"),rows[^1]);
            allowed=false;Notification(0x001A);await Task.Delay(150);Sample("policy disabled");save.Invoke(window,[Path.Combine(output,"02-disabled.png"),23]);
            check("system policy change releases glass and renders opaque fallback",!Available()&&!BackdropVisible()&&SurfaceField("nativeBackdrop") is null&&BackgroundColor()==Color.FromRgb(31,31,31),rows[^1]);
            check("policy disable releases old compositor references",References(original)==0,new{references=References(original)});
            using(var image=new System.Drawing.Bitmap(Path.Combine(output,"02-disabled.png")))
            {
                var dpi=VisualTreeHelper.GetDpi(window);var padding=(int)Math.Ceiling(23*dpi.DpiScaleX);var pixel=image.GetPixel(padding+(int)(window.ActualWidth*.6*dpi.DpiScaleX),padding+(int)(8*dpi.DpiScaleY));
                check("opaque fallback is visible in actual desktop pixels",pixel.R<=35&&pixel.G<=35&&pixel.B<=35,new{pixel.R,pixel.G,pixel.B});
            }
            allowed=true;Notification(0x031A);await Task.Delay(150);Sample("policy restored");save.Invoke(window,[Path.Combine(output,"03-restored.png"),23]);
            check("system policy restoration rebuilds glass without restarting",Available()&&BackdropVisible()&&SurfaceField("nativeBackdrop") is not null&&!ReferenceEquals(original,SurfaceField("nativeBackdrop"))&&BackgroundColor().A==1&&Field<bool>("nativeForegroundActive"),rows[^1]);
            check("restored actual compositor preserves fixed quarter blend",Math.Abs(Blend()-.25)<.00001,new{blend=Blend()});
            var stableHost=SurfaceField("nativeBackdrop");var beforeReads=reads;
            for(var i=0;i<30;i++)Notification(i%2==0?0x001A:0x031A);await Task.Delay(150);
            check("notification burst has one policy read and no redundant device rebuild",reads==beforeReads+1&&ReferenceEquals(stableHost,SurfaceField("nativeBackdrop")),new{beforeReads,reads});
            beforeReads=reads;for(var i=0;i<30;i++){Call("ApplyMaterial");Call("Render");}
            check("ordinary renders do not poll system settings",reads==beforeReads,new{beforeReads,reads});
            beforeReads=reads;await Task.Run(()=>surfaceType.GetMethod("PostPolicyRefresh",Flags)!.Invoke(surfaces,null));await Task.Delay(100);
            check("worker-thread event posts one policy refresh to owning dispatcher",reads==beforeReads+1,new{beforeReads,reads});
            var foreground=typeof(WindowsActivity).GetMethod("GetForegroundWindow",BindingFlags.Static|BindingFlags.NonPublic)!.CreateDelegate<Func<nint>>();
            var focus=foreground();window.Hide();allowed=false;Notification(0x001A);await Task.Delay(100);allowed=true;Notification(0x031A);await Task.Delay(100);
            check("policy updates preserve manual hiding and do not activate",!window.IsVisible&&!BackdropVisible()&&!ShadowVisible()&&foreground()==focus,new{visible=window.IsVisible,backdrop=BackdropVisible(),shadow=ShadowVisible(),focusUnchanged=foreground()==focus});
            window.Show();Call("Render");await Task.Delay(450);check("explicit show restores current glass",Available()&&BackdropVisible()&&Field<bool>("nativeForegroundActive"),null);
            var fullscreen=Field<FullscreenFixture>("fullscreenSource");fullscreen.Active=true;Call("RefreshFullscreenAvoidance");allowed=false;Notification(0x001A);await Task.Delay(100);allowed=true;Notification(0x031A);await Task.Delay(100);
            check("policy updates cannot reveal foreground backdrop or shadow during fullscreen suppression",Field<bool>("fullscreenSuppressed")&&!window.IsVisible&&!BackdropVisible()&&!ShadowVisible(),null);
            fullscreen.Active=false;Call("RefreshFullscreenAvoidance");await Task.Delay(1100);Call("RefreshFullscreenAvoidance");await Task.Delay(450);
            check("fullscreen clear restores glass with the same data state",window.IsVisible&&BackdropVisible()&&settings.BackgroundStyle=="glass"&&engine.Current.State=="tracking",null);
            unreadable=true;Notification(0x001A);await Task.Delay(100);
            check("unreadable policy safely uses opaque fallback",!Available()&&!BackdropVisible()&&BackgroundColor()==Color.FromRgb(31,31,31),new{result=surfaceType.GetProperty("BackdropResult")!.GetValue(surfaces)});
            unreadable=false;Notification(0x001A);await Task.Delay(100);check("policy-read recovery can restore glass",Available()&&BackdropVisible(),null);
            Call("ToggleExpanded",null,new RoutedEventArgs());await Task.Delay(300);Call("OpenSettings",window,new RoutedEventArgs());await Task.Delay(300);
            var settingsPanel=Field<FrameworkElement>("SettingsPage");var picker=Field<System.Windows.Controls.ComboBox>("BackgroundPicker");picker.IsDropDownOpen=true;
            allowed=false;Notification(0x001A);await Task.Delay(100);
            check("policy fallback keeps settings and its open picker",Field<bool>("isExpanded")&&ReferenceEquals(Field<FrameworkElement>("activePage"),settingsPanel)&&picker.IsDropDownOpen&&!Available(),null);
            allowed=true;Notification(0x031A);await Task.Delay(100);
            check("restoring effects keeps current settings and selection",Field<bool>("isExpanded")&&ReferenceEquals(Field<FrameworkElement>("activePage"),settingsPanel)&&picker.IsDropDownOpen&&Available()&&settings.BackgroundStyle=="glass",null);
            picker.IsDropDownOpen=false;Call("CollapseToTimer");await Task.Delay(300);
            check("settings return restores native foreground after policy changes",!Field<bool>("isExpanded")&&Field<bool>("nativeForegroundActive"),null);
            Call("EnableNativePulses");
            foreach(var mode in new[]{"paused","away"})
            {
                settings.Paused=mode=="paused";engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new("unmatched.exe","Unmatched",0));Call("Render");await Task.Delay(450);var epoch=Field<long>("statusPulseEpoch");
                allowed=false;Notification(0x001A);await Task.Delay(100);allowed=true;Notification(0x031A);await Task.Delay(100);
                check(mode+" policy replacement preserves pulse epoch and active renderer",engine.Current.State==mode&&Field<long>("statusPulseEpoch")==epoch&&Field<bool>("nativeForegroundActive")&&!Field<bool>("nativeForegroundFailed"),null);
            }
            settings.Paused=false;engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new("policy-fixture.exe","Policy fixture",0));Call("Render");await Task.Delay(450);
            var released=true;for(var i=0;i<8;i++){var host=SurfaceField("nativeBackdrop");allowed=false;Notification(0x001A);await Task.Delay(45);released&=References(host)==0;allowed=true;Notification(0x031A);await Task.Delay(45);released&=Available()&&Math.Abs(Blend()-.25)<.00001;}
            check("repeated policy reversals release every prior compositor and preserve blend",released,null);
            settings.BackgroundStyle="black";Call("ApplyMaterial");allowed=false;Notification(0x001A);await Task.Delay(100);allowed=true;Notification(0x031A);await Task.Delay(100);
            check("policy updates do not change chosen black style or reveal glass",settings.BackgroundStyle=="black"&&!BackdropVisible()&&ShadowVisible(),null);
            settings.BackgroundStyle="glass";Call("ApplyMaterial");await Task.Delay(100);check("returning to glass uses the restored backend",BackdropVisible()&&Math.Abs(Blend()-.25)<.00001,null);
            var label=Field<TextBlock>("TimeLabel");check("system material changes retain approved typography",label.FontFamily.Source=="Consolas"&&label.FontWeight==FontWeights.Bold&&label.FontSize==14,null);
            Call("SuspendNativeForeground");beforeReads=reads;Notification(0x001A);((IDisposable)surfaces).Dispose();await Task.Delay(100);
            check("disposing cancels pending reads and releases HWND material ownership",reads==beforeReads&&SurfaceField("policyRefresh") is null&&SurfaceField("nativeBackdrop") is null&&SurfaceField("backdrop") is null,null);
        }
        finally
        {
            typeof(IslandWindow).GetField("fidelityBackground",Flags)!.SetValue(window,null);background.Close();
            File.WriteAllText(Path.Combine(output,"system-material-policy.json"),JsonSerializer.Serialize(new{productDllSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),rows,scope="Isolated product HWND and data, injected read-only policy predicate, direct calls to the product source-hook callback with WM_SETTINGCHANGE/WM_THEMECHANGED identifiers. No OS setting change, registry write, broadcast, actual OS notification delivery or external input claim. Actual native compositor, fallback pixels and ownership objects observed."},AtomicStore.JsonOptions));
        }
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int PolicyGetFloat(nint self,out float value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int PolicyInvoke(nint self,nint sender,nint args);
}

