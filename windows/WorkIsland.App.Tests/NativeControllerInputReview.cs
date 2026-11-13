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
using System.Windows.Interop;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunNativeControllerInputReview(IslandWindow window,string output,Action<string,bool,object?> check,bool pulses=false)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        void Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var engine=Field<TrackingEngine>("engine");engine.Data.Settings.Rules=[new("input-controller.exe","Input fixture")];engine.Data.Settings.BackgroundStyle="black";engine.Data.Settings.BackgroundOpacity=.5;
        Field<DispatcherTimer>("fullscreenTimer").Stop();window.Left=320;window.Top=260;window.Title=pulses?"Work Island · 呼吸输入验收":"Work Island · 控制器输入验收";
        engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new(pulses?"other.exe":"input-controller.exe","Input fixture",0));Call("ApplyMaterial");Call("Render");
        if(pulses){typeof(IslandWindow).GetField("awaySince",Flags)!.SetValue(window,(DateTimeOffset?)DateTimeOffset.Now.AddSeconds(-11));Call("Render");}
        await Task.Delay(450);Call("EnableNativeForeground");if(pulses)Call("EnableNativePulses");await Task.Delay(100);
        var ready=Field<bool>("nativeForegroundActive");var downs=0;var expandClicks=0;var backClicks=0;var nativeAtExpand=true;
        window.PreviewMouseDown+=(_,_)=>downs++;
        Field<Button>("ExpandButton").Click+=(_,_)=>{expandClicks++;nativeAtExpand=Field<bool>("nativeForegroundActive");};
        var home=(StackPanel)Field<Border>("NavigationHome").Child;var back=((Grid)home.Children[0]).Children.OfType<Button>().Single();
        back.Click+=(_,_)=>backClicks++;
        var handle=new WindowInteropHelper(window).Handle;
        File.WriteAllText(Path.Combine(output,"input-ready.json"),JsonSerializer.Serialize(new{processId=RuntimeCompat.ProcessId,hwnd=handle.ToInt64(),title=window.Title,nativeAtReady=ready,window.ShowInTaskbar,timeoutSeconds=180,scope="Taskbar-visible isolated fixture with the real production controller. Listeners only observe actual mouse down and original button Click events; they do not invoke navigation, suspension or restoration."},AtomicStore.JsonOptions));
        var watch=Stopwatch.StartNew();
        while((expandClicks==0||backClicks==0)&&watch.Elapsed.TotalSeconds<180&&!File.Exists(Path.Combine(output,"stop-review.txt")))await Task.Delay(100);
        if(expandClicks>0&&backClicks>0)await Task.Delay(450);
        var nativeAfterBack=Field<bool>("nativeForegroundActive");var expanded=Field<bool>("isExpanded");
        check("actual mouse expands from native foreground and returns through original navigation",ready&&downs>=2&&expandClicks==1&&backClicks==1&&!nativeAtExpand&&!expanded&&nativeAfterBack,new{ready,downs,expandClicks,backClicks,nativeAtExpand,expanded,nativeAfterBack});
        File.WriteAllText(Path.Combine(output,"input-result.json"),JsonSerializer.Serialize(new{productDllSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),pulses,nativeMouseVerified=ready&&downs>=2&&expandClicks==1&&backClicks==1&&!nativeAtExpand&&!expanded&&nativeAfterBack,ready,downs,expandClicks,backClicks,nativeAtExpand,expanded,nativeAfterBack,window.ShowInTaskbar,scope="Actual native input and production handoff in taskbar-visible isolated window; pulses=true uses away glyph/cells controller. Not no-taskbar, tray or real-game acceptance."},AtomicStore.JsonOptions));
    }
}
