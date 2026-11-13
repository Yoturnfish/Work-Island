using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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
    private static async Task RunProfileOpeningLive(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        void Set(string name,object value)=>typeof(IslandWindow).GetField(name,Flags)!.SetValue(window,value);
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var source=Field<FullscreenFixture>("fullscreenSource");var engine=Field<TrackingEngine>("engine");var store=Field<AtomicStore>("store");
        var color=Field<Button>("RecognitionColorButton");var normalOpens=0;var nativeOpenings=0;var armed=false;
        var done=new TaskCompletionSource();
        async Task WaitFile(string name,double seconds)
        {
            var watch=Stopwatch.StartNew();while(!File.Exists(Path.Combine(output,name))){if(watch.Elapsed.TotalSeconds>seconds)throw new TimeoutException(name);await Task.Delay(75);}
        }
        void Write(string name,object value)=>File.WriteAllText(Path.Combine(output,name),JsonSerializer.Serialize(value,AtomicStore.JsonOptions));
        engine.Data.Settings.Paused=true;engine.Data.Settings.Rules=[];engine.Data.Settings.ReduceMotion=true;engine.Data.Settings.BackgroundStyle="black";
        engine.ResetObservation();Field<DispatcherTimer>("fullscreenTimer").Stop();Field<DispatcherTimer>("focusReturn").Stop();
        window.Title="Work Island · 组合右键实测 · "+RuntimeCompat.ProcessId;window.ShowInTaskbar=true;window.Topmost=false;window.Left=460;window.Top=300;
        Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenRecognition",window,new RoutedEventArgs());await Task.Delay(150);window.UpdateLayout();Call("Persist");
        var bytes=File.ReadAllBytes(store.Path);var memory=JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions);
        RoutedEventHandler click=(_,_)=>{if(color.ContextMenu?.IsOpen==true)normalOpens++;};
        ContextMenuEventHandler opening=(_,args)=>
        {
            if(!armed)return;nativeOpenings++;var handled=args.Handled;
            window.Dispatcher.InvokeAsync(async()=>
            {
                try
                {
                    await Task.Delay(220);
                    var evidence=new{normalOpens,nativeOpenings,handled,menuOpen=color.ContextMenu?.IsOpen==true,ownerVisible=window.IsVisible,suppressed=Field<bool>("fullscreenSuppressed"),windowHandle=new WindowInteropHelper(window).Handle.ToInt64()};
                    Write("native-observation.json",evidence);
                    check("official actual right click reaches context-menu opening",nativeOpenings==1,null);
                    check("native context menu is cancelled and remains closed during fullscreen",handled&&!evidence.menuOpen,evidence);
                    check("native context request refreshes suppression and hides owner",evidence.suppressed&&!evidence.ownerVisible,evidence);
                    done.TrySetResult();
                }
                catch(Exception error){done.TrySetException(error);}
            });
        };
        color.AddHandler(Button.ClickEvent,click,true);color.AddHandler(ContextMenuService.ContextMenuOpeningEvent,opening,true);
        try
        {
            Write("ready.json",new{processId=RuntimeCompat.ProcessId,startedUtcTicks=Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString(),windowHandle=new WindowInteropHelper(window).Handle.ToInt64(),window.Title,scope="Await official actual normal color click, Escape, then arm marker. Own controlled fullscreen source; no real game."});
            await WaitFile("arm-context.txt",180);
            check("official normal left click created a retained menu and Escape closed it",normalOpens==1&&color.ContextMenu is not null&&!color.ContextMenu.IsOpen,new{normalOpens,menuOpen=color.ContextMenu?.IsOpen});
            armed=true;source.Active=true;
            Write("armed.json",new{processId=RuntimeCompat.ProcessId,ownerVisible=window.IsVisible,pollStopped=!Field<DispatcherTimer>("fullscreenTimer").IsEnabled});
            await done.Task.WaitAsync(TimeSpan.FromSeconds(90));
            check("actual native context request preserves paused rules profiles and raw bytes",JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions)==memory&&File.ReadAllBytes(store.Path).AsSpan().SequenceEqual(bytes),null);
            await WaitFile("finish.txt",90);
        }
        finally
        {
            color.RemoveHandler(Button.ClickEvent,click);color.RemoveHandler(ContextMenuService.ContextMenuOpeningEvent,opening);
            if(color.ContextMenu is {} menu)menu.IsOpen=false;
            source.Active=false;Set("fullscreenSuppressed",false);Set("restoreAfterFullscreen",false);
            window.ShowActivated=false;window.Show();Call("CollapseToTimer");Field<DispatcherTimer>("focusReturn").Stop();await Task.Delay(350);
        }
    }
}
