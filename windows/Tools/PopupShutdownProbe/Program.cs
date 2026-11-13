using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

// Independent diagnostic: deliberately has no WorkIsland assembly reference,
// state store, input injection, system-setting changes or process termination.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if(args.Length!=2||args[1] is not ("immediate" or "idle" or "retained" or "settled" or "pointer-immediate"))return 2;
        // Diagnostic-only opt-in, before any WPF input initialization. This
        // changes neither product configuration nor machine/user registry.
        if(args[1]=="pointer-immediate")AppContext.SetSwitch("Switch.System.Windows.Input.Stylus.EnablePointerSupport",true);
        var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
        var trace=Path.Combine(output,"events.jsonl");
        if(File.Exists(trace))return 2;
        var clock=Stopwatch.StartNew();
        void Log(string stage,object? evidence=null)=>File.AppendAllText(trace,JsonSerializer.Serialize(new{stage,seconds=clock.Elapsed.TotalSeconds,thread=Environment.CurrentManagedThreadId,capture=Mouse.Captured?.GetType().FullName,evidence})+Environment.NewLine);
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        var anchor=new Button{Content="Owned popup shutdown diagnostic",Width=250,Height=36};
        var window=new Window{Title="Work Island · independent popup shutdown probe",Content=anchor,Width=300,Height=100,Left=40,Top=150,ShowActivated=false,ShowInTaskbar=false,Topmost=false};
        var opened=0;var closed=0;var unloaded=0;ContextMenu? current=null;
        var errors=new List<string>();
        string? observedStack=null;
        app.DispatcherUnhandledException+=(_,e)=>{File.WriteAllText(Path.Combine(output,"unhandled.txt"),e.Exception.ToString());};
        window.Closed+=(_,_)=>Log("window-closed");
        app.Exit+=(_,_)=>Log("application-exit");
        app.Dispatcher.ShutdownStarted+=(_,_)=>Log("dispatcher-shutdown-started");
        app.Dispatcher.ShutdownFinished+=(_,_)=>Log("dispatcher-shutdown-finished");
        app.Dispatcher.InvokeAsync(async ()=>
        {
            try
            {
                window.Show();await Task.Delay(100);
                var logic=typeof(InputManager).Assembly.GetType("System.Windows.Input.StylusLogic")!;
                observedStack=logic.GetField("_currentStylusLogic",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)?.GetType().FullName;
                var support=(bool)logic.GetProperty("IsStylusAndTouchSupportEnabled",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
                AppContext.TryGetSwitch("Switch.System.Windows.Input.Stylus.EnablePointerSupport",out var pointerSwitch);
                if(args[1]=="pointer-immediate"&&(!pointerSwitch||observedStack!="System.Windows.Input.StylusPointer.PointerLogic"||!support))errors.Add("Requested pointer input stack or enabled touch/stylus support not established");
                Log("ready",new{pid=Environment.ProcessId,mode=args[1],runtime=Environment.Version.ToString(),core=RuntimeEnvironment.GetRuntimeDirectory(),wpf=typeof(Window).Assembly.Location,presentationCore=typeof(InputManager).Assembly.Location,presentationCoreSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(InputManager).Assembly.Location))),assembly=Assembly.GetExecutingAssembly().Location,observedStack,pointerSwitch,stylusAndTouchSupportEnabled=support});
                for(var round=0;round<8;round++)
                {
                    var panel=new StackPanel{Width=250};panel.Children.Add(new TextBox{Text="Synthetic name"});panel.Children.Add(new Button{Content="Synthetic style"});
                    var closeDone=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var unloadDone=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    current=new ContextMenu{PlacementTarget=anchor,Placement=PlacementMode.Bottom,StaysOpen=true};
                    current.Items.Add(new MenuItem{Header=panel,StaysOpenOnClick=true,Focusable=false});
                    current.Opened+=(_,_)=>opened++;
                    current.Closed+=(_,_)=>{closed++;closeDone.TrySetResult();};
                    panel.Unloaded+=(_,_)=>{unloaded++;unloadDone.TrySetResult();};
                    current.IsOpen=true;await Task.Delay(35);
                    if(!current.IsOpen||!current.IsVisible)errors.Add("Popup not actually visible at round "+round);
                    if(round==7)
                    {
                        Log("last-popup-open",new{opened,closed,unloaded,current.IsOpen,current.IsVisible});
                        if(args[1]!="retained")current.IsOpen=false;
                        Log("last-popup-close-request",new{opened,closed,unloaded,current.IsOpen,current.IsVisible});
                        if(args[1]=="settled")
                        {
                            var teardown=await WpfPopupTeardown.CloseAsync(new[]{current},TimeSpan.FromSeconds(3));
                            Log("last-popup-settled",new{opened,closed,unloaded,current.IsOpen,current.IsVisible,teardown});
                            if(!teardown.Passed)errors.Add("Popup teardown did not release visuals and capture");
                        }
                        if(args[1]=="idle")
                        {
                            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                            Log("context-idle-drained",new{opened,closed,unloaded,current.IsOpen,current.IsVisible});
                        }
                        window.Close();Log("before-explicit-shutdown",new{opened,closed,unloaded,errors});
                        app.Shutdown(errors.Count==0?0:1);Log("shutdown-call-returned");return;
                    }
                    current.IsOpen=false;
                    if(args[1]=="settled")await WpfPopupTeardown.CloseAsync(new[]{current},TimeSpan.FromSeconds(3));
                    await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                }
            }
            catch(Exception error){Log("probe-failed",error.ToString());errors.Add(error.ToString());current?.SetCurrentValue(ContextMenu.IsOpenProperty,false);window.Close();app.Shutdown(1);}
        });
        var code=app.Run();
        Log("run-returned",new{code,opened,closed,unloaded,errors});
        File.WriteAllText(Path.Combine(output,"probe-result.json"),JsonSerializer.Serialize(new{passed=code==0&&errors.Count==0,code,opened,closed,unloaded,mode=args[1],observedStack,scope="Independent WPF, eight actual popups; no product reference or physical input. A terminal successful run does not prove all product shutdown timings safe or touch/pen compatibility."},new JsonSerializerOptions{WriteIndented=true}));
        return code;
    }
}
