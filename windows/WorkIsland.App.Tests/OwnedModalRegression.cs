using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static Process StartOwnTestProbe(string mode,string folder)
    {
        var info=new ProcessStartInfo(RuntimeCompat.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
        if(Path.GetFileNameWithoutExtension(RuntimeCompat.ProcessPath)=="dotnet")TestRuntime.AddArgument(info,Assembly.GetExecutingAssembly().Location);
        TestRuntime.AddArgument(info,mode);TestRuntime.AddArgument(info,folder);return Process.Start(info)!;
    }
    private static async Task RunOwnedModalChecks(string output,Action<string,bool,object?> check)
    {
        foreach(var mode in new[]{"floating","menuBar"})
        {
            var folder=Path.Combine(output,"owned-modal",mode);Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"mode.txt"),mode);
            using var child=StartOwnTestProbe("--owned-modal-probe",folder);
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(40));
            await child.WaitForExitAsync(timeout.Token);
            var report=JsonSerializer.Deserialize<OwnedModalReport>(File.ReadAllText(Path.Combine(folder,"native-modal.json")),AtomicStore.JsonOptions)!;
            foreach(var item in report.Checks)check(mode+": "+item.Name,item.Passed,item.Evidence);
            check(mode+": isolated modal fixture exits normally",child.ExitCode==0,new{child.ExitCode});
        }
    }
    private sealed record OwnedModalCheck(string Name,bool Passed,object? Evidence);
    private sealed record OwnedModalReport(bool Passed,List<OwnedModalCheck> Checks,string? CleanupError,string Limitation);
    private static int RunOwnedModalProbe(string folder)
    {
        Directory.CreateDirectory(folder);var screenFolder=Path.Combine(folder,"screen-fixture");Directory.CreateDirectory(screenFolder);
        var command=Path.Combine(screenFolder,"probe-command.txt");var ready=Path.Combine(screenFolder,"probe-ready.txt");File.WriteAllText(command,"normal");
        using var screen=StartOwnTestProbe("--fullscreen-native-probe",screenFolder);
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        var checks=new List<OwnedModalCheck>();void Check(string name,bool passed,object? evidence=null)=>checks.Add(new(name,passed,evidence));
        var mode=File.Exists(Path.Combine(folder,"mode.txt"))?File.ReadAllText(Path.Combine(folder,"mode.txt")):"floating";
        var observeInitial=File.Exists(Path.Combine(folder,"observe-initial-only.txt"));double? firstDialogAt=null;var sawInitialHidden=false;
        var traceNative=File.Exists(Path.Combine(folder,"trace-native-dialog.txt"));
        var tracePicker=traceNative?new ModalNativeTracePicker(folder):null;
        var neverVisible=File.Exists(Path.Combine(folder,"native-dialog-never-visible.txt"));
        var language=mode=="floating"?"en":"zh";
        var state=new AppState{Settings=new(){Rules=[],WindowX=320,WindowY=240,DisplayMode=mode,Language=language}};
        var island=new IslandWindow(new AtomicStore(Path.Combine(folder,"state.json")),state,false,null,
            startupRegistration:new IsolatedStartupRegistration(Path.Combine(folder,"startup.json"),"isolated modal test"),applicationPicker:tracePicker);
        island.Title="Work Island · Owned native dialog fixture";island.ShowActivated=false;island.Topmost=false;
        var watch=Stopwatch.StartNew();var phase=0;double phaseAt=0;nint dialog=0,owner=0,foreground=0;bool closed=false;string? cleanupError=null;
        var observer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(60)};
        if(observeInitial)observer.Interval=TimeSpan.FromMilliseconds(10);
        string? Ready(){try{return File.Exists(ready)?File.ReadAllText(ready):null;}catch(IOException){return null;}}
        void CloseOwnedFixture()
        {
            if(closed)return;closed=true;observer.Stop();File.WriteAllText(command,"exit");
            // Test teardown closes this fixture's WPF owner. Windows destroys
            // its owned dialog; no dialog input, message injection or kill.
            typeof(IslandWindow).GetField("exiting",Flags)!.SetValue(island,true);island.Close();
        }
        EventHandler observeDialog=(_,_)=>
        {
            try
            {
                var elapsed=watch.Elapsed.TotalSeconds;
                if(elapsed>22){Check("modal test completes within its bounded own-fixture lifetime",false,new{phase});CloseOwnedFixture();return;}
                if(phase==0)
                {
                    dialog=FindOwnNativeDialog(owner);
                    if(dialog==0)return;
                    var nativeVisible=ModalIsWindowVisible(dialog);var ownerVisible=island.IsVisible;
                    if(neverVisible&&nativeVisible)
                    {
                        // Fault injection touches only the already matched,
                        // same-process/thread owner HWND's native test dialog.
                        ModalTraceHideWindow(dialog,0);nativeVisible=ModalIsWindowVisible(dialog);
                        File.AppendAllText(Path.Combine(folder,"forced-hidden.jsonl"),JsonSerializer.Serialize(new{elapsed,dialog=(long)dialog,owner=(long)owner,nativeVisible,ownerVisible})+Environment.NewLine);
                    }
                    if(observeInitial)
                    {
                        firstDialogAt??=elapsed;sawInitialHidden|=!nativeVisible;
                        var suppressed=(bool)typeof(IslandWindow).GetField("fullscreenSuppressed",Flags)!.GetValue(island)!;
                        var popupsSuppressed=(bool)typeof(IslandWindow).GetField("ownedPopupsSuppressed",Flags)!.GetValue(island)!;
                        File.AppendAllText(Path.Combine(folder,"initial-visibility-timeline.jsonl"),JsonSerializer.Serialize(new{elapsed,firstDialogAt,dialog=(long)dialog,owner=(long)owner,nativeVisible,ownerVisible,suppressed,popupsSuppressed,probeReady=Ready()})+Environment.NewLine);
                        if(!nativeVisible||elapsed-firstDialogAt.Value<.12)return;
                        Check("initial dialog becomes visible without fullscreen or explicit show",ownerVisible&&!suppressed&&!popupsSuppressed&&Ready()=="normal",new{sawInitialHidden,firstDialogAt,visibleAt=elapsed});
                        CloseOwnedFixture();return;
                    }
                    File.AppendAllText(Path.Combine(folder,"modal-readiness.jsonl"),JsonSerializer.Serialize(new{elapsed,dialog=(long)dialog,owner=(long)owner,nativeVisible,ownerVisible,probeReady=Ready(),suppressed=(bool)typeof(IslandWindow).GetField("fullscreenSuppressed",Flags)!.GetValue(island)!})+Environment.NewLine);
                    // Finding an owned HWND is not proof that IFileDialog has
                    // finished initialization. Trace shows the same enumerated
                    // HWND is initially hidden and later raises EVENT_SHOW.
                    // Keep the original 22s lifetime and fail immediately for
                    // a hidden owner/product suppression; only creation waits.
                    if(!nativeVisible&&ownerVisible&&!(bool)typeof(IslandWindow).GetField("fullscreenSuppressed",Flags)!.GetValue(island)!
                        &&!(bool)typeof(IslandWindow).GetField("ownedPopupsSuppressed",Flags)!.GetValue(island)!)return;
                    if(!nativeVisible||!ownerVisible)FullscreenReadOnly.Write(Path.Combine(folder,"initial-hidden-fullscreen-state.json"));
                    Check("actual Windows file picker is visible and owned by the isolated island",ModalIsWindowVisible(dialog)&&island.IsVisible,new{dialog=(long)dialog,owner=(long)owner,processId=RuntimeCompat.ProcessId});
                    var dialogTitle=new StringBuilder(512);LocalizedDialogTitle(dialog,dialogTitle,dialogTitle.Capacity);
                    Check("actual native picker title follows the selected interface language",dialogTitle.ToString()==(language=="en"?"Choose an app to track":"选择需要识别的应用"),new{language,title=dialogTitle.ToString()});
                    File.WriteAllText(command,"fullscreen");phase=1;
                }
                else if(phase==1&&Ready()=="fullscreen"){phaseAt=elapsed;phase=2;}
                else if(phase==2&&elapsed-phaseAt>.8)
                {
                    Check("separate native fullscreen is detected while the file chooser is modal",new WindowsFullscreenSource().IsFullscreenActive(),null);
                    Check("fullscreen hides island and its actual native file picker",!island.IsVisible&&!ModalIsWindowVisible(dialog),new{ownerVisible=island.IsVisible,nativeDialogVisible=ModalIsWindowVisible(dialog)});
                    File.WriteAllText(command,"minimized");phase=3;
                }
                else if(phase==3&&Ready()=="minimized"){foreground=ModalGetForegroundWindow();phaseAt=elapsed;phase=4;}
                else if(phase==4&&elapsed-phaseAt>1.7)
                {
                    var stillFullscreen=new WindowsFullscreenSource().IsFullscreenActive();
                    var suppressed=(bool)typeof(IslandWindow).GetField("fullscreenSuppressed",Flags)!.GetValue(island)!;
                    if((stillFullscreen||suppressed||mode=="floating"&&!island.IsVisible)&&elapsed-phaseAt<7){foreground=ModalGetForegroundWindow();return;}
                    Check("minimized fixture clears actual system fullscreen state",!stillFullscreen&&!suppressed,new{stillFullscreen,suppressed,elapsedAfterMinimize=elapsed-phaseAt});
                    if(mode=="menuBar")
                    {
                        Check("clear fullscreen keeps temporary menu panel and picker hidden",!island.IsVisible&&!ModalIsWindowVisible(dialog),null);
                        typeof(IslandWindow).GetMethod("OpenMenuPanel",Flags)!.Invoke(island,null);
                    }
                    Check("restores the same native picker and recognition page",island.IsVisible&&ModalIsWindowVisible(dialog)&&typeof(IslandWindow).GetField("activePage",Flags)!.GetValue(island) is FrameworkElement{Name:"RecognitionPage"},new{ownerVisible=island.IsVisible,nativeDialogVisible=ModalIsWindowVisible(dialog)});
                    if(mode=="floating")Check("native picker restore does not activate over the previous foreground",ModalGetForegroundWindow()==foreground,new{before=(long)foreground,after=(long)ModalGetForegroundWindow()});
                    typeof(IslandWindow).GetMethod("HideIsland",Flags)!.Invoke(island,[null,new RoutedEventArgs()]);
                    Check("manual hide suppresses the native modal picker too",!island.IsVisible&&!ModalIsWindowVisible(dialog),null);
                    typeof(IslandWindow).GetMethod("ShowIsland",Flags)!.Invoke(island,null);
                    Check("explicit show resumes the native modal picker on the original recognition page",island.IsVisible&&ModalIsWindowVisible(dialog)&&typeof(IslandWindow).GetField("activePage",Flags)!.GetValue(island) is FrameworkElement{Name:"RecognitionPage"},null);
                    CloseOwnedFixture();
                }
            }
            catch(Exception error){cleanupError=error.ToString();Check("modal observer remains healthy",false,cleanupError);CloseOwnedFixture();}
        };
        observer.Tick+=observeDialog;
        // Exercise the readiness path at the real native creation event,
        // without delaying, hiding or showing the dialog ourselves.
        if(tracePicker is not null&&File.Exists(Path.Combine(folder,"observe-at-native-create.txt")))
            tracePicker.OnNativeCreated=()=>observeDialog(null,EventArgs.Empty);
        island.Loaded+=(_,_)=>
        {
            owner=new WindowInteropHelper(island).Handle;
            _=island.Dispatcher.BeginInvoke(async()=>
            {
                try
                {
                    var deadline=Stopwatch.StartNew();while(Ready()!="normal"&&deadline.Elapsed.TotalSeconds<5)await Task.Delay(60);
                    if(mode=="menuBar")typeof(IslandWindow).GetMethod("OpenMenuPanel",Flags)!.Invoke(island,null);
                    else typeof(IslandWindow).GetMethod("ToggleExpanded",Flags)!.Invoke(island,[null,new RoutedEventArgs()]);
                    typeof(IslandWindow).GetMethod("OpenRecognition",Flags)!.Invoke(island,[island,new RoutedEventArgs()]);
                    observer.Start();
                    typeof(IslandWindow).GetMethod("ChooseApplication",Flags)!.Invoke(island,[island,new RoutedEventArgs()]);
                    Check("native dialog teardown preserves application rules and releases modal state",state.Settings.Rules.Count==0&&!(bool)typeof(IslandWindow).GetField("applicationChooserOpen",Flags)!.GetValue(island)!,null);
                }
                catch(Exception error){cleanupError=error.ToString();Check("native picker returns from the owned fixture",false,cleanupError);}
                finally
                {
                    CloseOwnedFixture();File.WriteAllText(command,"exit");
                    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(28));
                    try{await screen.WaitForExitAsync(timeout.Token);Check("separate fullscreen probe exits normally",screen.ExitCode==0,new{screen.ExitCode});}
                    catch(OperationCanceledException){Check("fullscreen probe exits within its own watchdog lifetime",false,null);}
                    File.WriteAllText(Path.Combine(folder,"native-modal.json"),JsonSerializer.Serialize(new OwnedModalReport(checks.All(c=>c.Passed),checks,cleanupError,"Actual Windows OpenFileDialog and a separate native fullscreen fixture. No real game, system session, file selection or external app input."),AtomicStore.JsonOptions));
                    app.Shutdown();
                }
            },DispatcherPriority.ApplicationIdle);
        };
        app.Run(island);return checks.All(c=>c.Passed)?0:1;
    }
    private static nint FindOwnNativeDialog(nint owner)
    {
        nint result=0;
        ModalEnumThreadWindows(ModalGetCurrentThreadId(),(window,_)=>
        {
            ModalGetWindowThreadProcessId(window,out var pid);var name=new StringBuilder(128);ModalGetClassName(window,name,name.Capacity);
            if(pid==RuntimeCompat.ProcessId&&name.ToString()=="#32770"&&ModalGetWindow(window,4)==owner){result=window;return false;}return true;
        },0);return result;
    }
    [DllImport("user32.dll",EntryPoint="ShowWindow")]private static extern bool ModalTraceHideWindow(nint window,int command);
    private delegate bool ModalEnumProc(nint window,nint parameter);
    [DllImport("user32.dll",EntryPoint="GetWindowTextW",CharSet=CharSet.Unicode)]private static extern int LocalizedDialogTitle(nint window,StringBuilder value,int length);
    [DllImport("user32.dll",EntryPoint="EnumThreadWindows")]private static extern bool ModalEnumThreadWindows(uint thread,ModalEnumProc callback,nint parameter);
    [DllImport("kernel32.dll",EntryPoint="GetCurrentThreadId")]private static extern uint ModalGetCurrentThreadId();
    [DllImport("user32.dll",EntryPoint="GetWindowThreadProcessId")]private static extern uint ModalGetWindowThreadProcessId(nint window,out uint pid);
    [DllImport("user32.dll",EntryPoint="GetClassNameW",CharSet=CharSet.Unicode)]private static extern int ModalGetClassName(nint window,StringBuilder name,int length);
    [DllImport("user32.dll",EntryPoint="GetWindow")]private static extern nint ModalGetWindow(nint window,uint command);
    [DllImport("user32.dll",EntryPoint="IsWindowVisible")]private static extern bool ModalIsWindowVisible(nint window);
    [DllImport("user32.dll",EntryPoint="GetForegroundWindow")]private static extern nint ModalGetForegroundWindow();
}
