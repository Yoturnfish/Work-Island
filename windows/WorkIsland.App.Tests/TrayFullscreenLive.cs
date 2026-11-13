using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;
using Forms=System.Windows.Forms;

internal static partial class Program
{
    private static async Task RunTrayFullscreenLive(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        var engine=Field<TrackingEngine>("engine");engine.Data.Settings.Paused=true;engine.Data.Settings.BackgroundStyle="black";engine.ResetObservation();
        typeof(IslandWindow).GetMethod("Render",Flags|BindingFlags.DeclaredOnly)!.Invoke(window,null);
        window.Title="Work Island · 全屏托盘验收";window.ShowInTaskbar=true;
        typeof(IslandWindow).Assembly.GetType("WorkIsland.App.InteractionReview")!.GetMethod("Open",BindingFlags.Static|BindingFlags.Public)!.Invoke(null,[window,output]);
        Field<DispatcherTimer>("fullscreenTimer").Stop();Field<DispatcherTimer>("timer").Stop();
        var source=Field<FullscreenFixture>("fullscreenSource");source.Active=true;
        var menu=Field<Forms.NotifyIcon>("tray").ContextMenuStrip!;var requests=0;var cancelled=0;var opened=0;
        CancelEventHandler opening=(_,args)=>{requests++;if(args.Cancel)cancelled++;};EventHandler onOpened=(_,_)=>opened++;
        menu.Opening+=opening;menu.Opened+=onOpened;
        File.WriteAllText(Path.Combine(output,"input-ready.json"),JsonSerializer.Serialize(new{processId=Environment.ProcessId,windowHandle=new WindowInteropHelper(window).Handle.ToInt64(),window.Title,scope="Real official right-click on own InteractionReview background forwards the production tray menu request. FullscreenFixture=true, both timers stopped for controlled observation; not a game. After preparation only original product event handlers, no direct menu opening by fixture."},AtomicStore.JsonOptions));
        try
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();
            while(watch.Elapsed.TotalSeconds<180&&!File.Exists(Path.Combine(output,"stop-review.txt")))await Task.Delay(100);
            check("actual own-background right-click reaches shared tray Opening",requests>0,null);
            check("native context request is cancelled before any menu opens",requests>0&&cancelled==requests&&opened==0&&!menu.Visible,new{requests,cancelled,opened,menu.Visible});
            check("native request suppresses island and preserves pause",Field<bool>("fullscreenSuppressed")&&!window.IsVisible&&engine.Data.Settings.Paused,null);
            File.WriteAllText(Path.Combine(output,"tray-fullscreen-live.json"),JsonSerializer.Serialize(new{appSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),coreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(TrackingEngine).Assembly.Location))),requests,cancelled,opened,menu.Visible,windowVisible=window.IsVisible,fullscreenSuppressed=Field<bool>("fullscreenSuppressed"),scope="Official actual right-click on own background, original production tray review forwarding and menu Opening guard. Fullscreen is controlled provider only; native popup visibility and WPF owner read, no real game or shell tray-icon gesture acceptance."},AtomicStore.JsonOptions));
        }
        finally{menu.Opening-=opening;menu.Opened-=onOpened;menu.Close();Field<DispatcherTimer>("focusReturn").Stop();await Task.Delay(250);}
    }
}
