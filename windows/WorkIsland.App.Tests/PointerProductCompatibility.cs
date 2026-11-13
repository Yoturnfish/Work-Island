using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static bool ObservePointerBackend(string output,Action<string,bool,object?> check)
    {
        const string name="Switch.System.Windows.Input.Stylus.EnablePointerSupport";
        var requested=AppContext.TryGetSwitch(name,out var enabled)&&enabled;
        var logic=typeof(InputManager).Assembly.GetType("System.Windows.Input.StylusLogic")!;
        var actual=logic.GetField("_currentStylusLogic",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)?.GetType().FullName;
        var support=(bool)logic.GetProperty("IsStylusAndTouchSupportEnabled",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        check("candidate runtime requests pointer input support",requested,null);
        check("candidate process actually uses PointerLogic",actual=="System.Windows.Input.StylusPointer.PointerLogic",actual);
        check("candidate keeps touch and stylus support enabled",support,null);
        File.WriteAllText(Path.Combine(output,"input-backend.json"),JsonSerializer.Serialize(new{processId=Environment.ProcessId,requested,actual,stylusAndTouchSupportEnabled=support,runtime=Environment.Version.ToString(),runtimeDirectory=RuntimeEnvironment.GetRuntimeDirectory(),presentationCore=typeof(InputManager).Assembly.Location,presentationCoreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(InputManager).Assembly.Location))),appSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),scope="Candidate test process actual input backend after product window creation. Runtimeconfig opt-in is limited to copied binaries. Enabled support is not physical touch/pen acceptance; child executables configured separately, not individually reflected here."},AtomicStore.JsonOptions));
        return requested&&actual=="System.Windows.Input.StylusPointer.PointerLogic"&&support;
    }
    private static async Task RunPointerProductPopupExit(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var settings=Field<TrackingEngine>("engine").Data.Settings;settings.Paused=true;settings.Rules=[];settings.HasCompletedOnboarding=true;settings.ReduceMotion=false;
        Call("Render");Call("CollapseToTimer");await Task.Delay(350);Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenRecognition",window,new RoutedEventArgs());await Task.Delay(300);
        var rows=new List<object>();ContextMenu? last=null;var opened=0;
        for(var round=0;round<8;round++)
        {
            Call("OpenProfilePopover");last=Field<Button>("RecognitionColorButton").ContextMenu!;
            await Task.Delay(35);var visible=last.IsOpen&&last.IsVisible;if(visible)opened++;
            check("candidate product popup actually opens round "+round,visible,null);
            rows.Add(new{round,last.IsOpen,last.IsVisible,last.IsLoaded,capture=Mouse.Captured?.GetType().FullName});
            // Deliberately do not use the settled test barrier. Keep the final
            // real product menu open while requesting its normal saved exit.
            if(round<7){last.IsOpen=false;await Task.Delay(20);}
        }
        var before=new{opened,last!.IsOpen,last.IsVisible,last.IsLoaded,capture=Mouse.Captured?.GetType().FullName};
        check("candidate normal exit begins with actual open product popup",opened==8&&last.IsOpen&&last.IsVisible&&Mouse.Captured is ContextMenu,before);
        var prepared=(bool)Call("PrepareNormalExit")!;check("candidate product prepares saved normal exit with popup open",prepared,null);
        File.WriteAllText(Path.Combine(output,"popup-before-exit.json"),JsonSerializer.Serialize(new{before,rows,prepared,scope="Exact product WPF window and actual production profile menus. Fixture calls product handlers, not physical input. No settled Closed/Unloaded wait; final popup intentionally open. Process exit must be observed separately."},AtomicStore.JsonOptions));
        if(!prepared)throw new InvalidOperationException("Retain candidate fixture: product refused normal saved exit");
        var watch=Stopwatch.StartNew();Call("CompleteNormalExit");
        File.WriteAllText(Path.Combine(output,"popup-window-close.json"),JsonSerializer.Serialize(new{seconds=watch.Elapsed.TotalSeconds,windowVisible=window.IsVisible,scope="Product close method returned; not proof Application.Run or process returned."},AtomicStore.JsonOptions));
    }
}
