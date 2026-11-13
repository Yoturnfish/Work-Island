using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal sealed class ClearFullscreen : IFullscreenSource
{ public bool IsFullscreenActive()=>false; }

internal static class Program
{
    [STAThread] private static int Main(string[] args)
    {
        if(args.Length!=2||args[1] is not ("immediate" or "settled"))return 2;
        var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
        var trace=Path.Combine(output,"events.jsonl");if(File.Exists(trace))return 2;
        var clock=Stopwatch.StartNew();
        void Log(string stage,object? evidence=null)=>File.AppendAllText(trace,JsonSerializer.Serialize(new{stage,seconds=clock.Elapsed.TotalSeconds,capture=Mouse.Captured?.GetType().FullName,evidence})+Environment.NewLine);
        var data=Path.Combine(output,"isolated-data");Directory.CreateDirectory(data);
        var state=new AppState{Settings=new(){Rules=[],Paused=true,HasCompletedOnboarding=true,WindowX=40,WindowY=300}};
        var store=new AtomicStore(Path.Combine(data,"state.json"));
        var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};
        var window=new IslandWindow(store,state,false,null,startupRegistration:new IsolatedStartupRegistration(Path.Combine(data,"startup-fixture.json"),"probe-only"),fullscreenSource:new ClearFullscreen());
        app.MainWindow=window;window.ShowActivated=false;window.ShowInTaskbar=false;window.Topmost=false;window.Title="Work Island · product popup exit probe";
        InstanceControlServer? control=null;var opened=0;var closed=0;var requestPrepared=false;
        window.Closed+=(_,_)=>{Log("window-closed");control?.Dispose();};
        app.Exit+=(_,_)=>Log("application-exit");
        app.Dispatcher.ShutdownStarted+=(_,_)=>Log("dispatcher-shutdown-started");
        app.Dispatcher.ShutdownFinished+=(_,_)=>Log("dispatcher-shutdown-finished");
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        window.Loaded+=(_,_)=>
        {
            window.Topmost=false;
            typeof(IslandWindow).GetMethod("EnableNativeForeground",flags)!.Invoke(window,null);
            typeof(IslandWindow).GetMethod("EnableNativePulses",flags)!.Invoke(window,null);
            control=new InstanceControlServer(window,data);
            window.Dispatcher.InvokeAsync(async ()=>
            {
                try
                {
                    await Task.Delay(150);
                    ((Button)window.FindName("ExpandButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Task.Delay(400);
                    ((Button)window.FindName("RecognitionNavigationButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Task.Delay(400);
                    var color=(Button)window.FindName("RecognitionColorButton");
                    for(var i=0;i<8;i++)
                    {
                        color.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var menu=color.ContextMenu??throw new Exception("No actual product menu");
                        var closure=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        // Count verified visible rounds, not Opened events that
                        // may have fired synchronously before this observation.
                        menu.Closed+=(_,_)=>{closed++;closure.TrySetResult();};
                        await Task.Delay(35);
                        if(!menu.IsOpen||!menu.IsVisible)throw new Exception("Product menu not visible at round "+i);
                        opened++;
                        if(i==7)Log("last-product-popup-open",new{opened,closed,menu.IsOpen,menu.IsVisible});
                        menu.IsOpen=false;
                        if(i==7)Log("last-product-popup-close-request",new{opened,closed,menu.IsOpen,menu.IsVisible});
                        if(args[1]=="settled")
                        {
                            await closure.Task.WaitAsync(TimeSpan.FromSeconds(3));
                            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                        }
                    }
                    using var own=Process.GetCurrentProcess();
                    Log("before-product-exit-request",new{opened,closed,mode=args[1],pid=own.Id,startTicks=own.StartTime.ToUniversalTime().Ticks.ToString(),app=typeof(IslandWindow).Assembly.Location});
                    var response=await InstanceControlClient.RequestAsync(data,new("exit",own.Id,own.StartTime.ToUniversalTime().Ticks.ToString()));
                    requestPrepared=response.Success&&response.Status=="exit-prepared";
                    Log("product-exit-response",new{response.Success,response.Status});
                }
                catch(Exception error){Log("probe-error",error.ToString());}
            });
        };
        var code=app.Run(window);Log("run-returned",new{code,opened,closed,requestPrepared});control?.Dispose();
        File.WriteAllText(Path.Combine(output,"probe-result.json"),JsonSerializer.Serialize(new{passed=code==0,code,opened,closed,requestPrepared,scope="Published 2104 DLL/Core, controlled WPF routed clicks and fullscreen-clear source, actual product instance-control save/exit and OnMainWindowClose. Native bridge enabled. Not ordinary EXE startup or physical user input."},new JsonSerializerOptions{WriteIndented=true}));
        return code;
    }
}
