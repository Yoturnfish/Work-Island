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
    private static async Task RunNativeModeInput(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var settings=Field<TrackingEngine>("engine").Data.Settings;settings.Paused=true;settings.ReduceMotion=true;settings.Language="zh";settings.BackgroundStyle="black";settings.BackgroundOpacity=1;
        Field<DispatcherTimer>("fullscreenTimer").Stop();window.Left=640;window.Top=300;window.Title="Work Island · 显示模式输入验收";
        var floatingX=window.Left;var floatingY=window.Top;var picker=Field<ComboBox>("DisplayPicker");
        // Controlled initial state only; the mode switch below must come from
        // actual native input, not a routed event, setter, or Shell callback.
        picker.SelectedItem=picker.Items.Cast<ComboBoxItem>().Single(i=>i.Tag?.ToString()=="menuBar");Call("OpenMenuPanel");await Task.Delay(150);
        var status=Field<object>("statusItem");bool Installed()=>(bool)status.GetType().GetProperty("Installed")!.GetValue(status)!;
        var initialInstalled=Installed();var mouseDowns=0;var keyDowns=0;var opens=0;var selections=0;var settingsClicks=0;
        window.PreviewMouseDown+=(_,_)=>mouseDowns++;window.PreviewKeyDown+=(_,_)=>keyDowns++;picker.DropDownOpened+=(_,_)=>opens++;picker.SelectionChanged+=(_,_)=>selections++;
        Field<Button>("SettingsNavigationButton").Click+=(_,_)=>settingsClicks++;
        var hwnd=new WindowInteropHelper(window).Handle;var watch=Stopwatch.StartNew();
        File.WriteAllText(Path.Combine(output,"input-ready.json"),JsonSerializer.Serialize(new{processId=RuntimeCompat.ProcessId,hwnd=hwnd.ToInt64(),title=window.Title,initialMode=settings.DisplayMode,initialInstalled,window.ShowInTaskbar,timeoutSeconds=180,scope="Controlled initial menu mode and internal panel open; pending mode switch requires official native input."},AtomicStore.JsonOptions));
        while(settings.DisplayMode!="floating"&&watch.Elapsed.TotalSeconds<180&&!File.Exists(Path.Combine(output,"stop-review.txt")))await Task.Delay(100);
        await Task.Delay(350);
        var saved=Field<AtomicStore>("store").Load();var positionRestored=Math.Abs(window.Left-floatingX)<1&&Math.Abs(window.Top-floatingY)<1;
        var passed=initialInstalled&&settings.DisplayMode=="floating"&&saved.Settings.DisplayMode=="floating"&&window.IsVisible&&!Installed()&&positionRestored&&opens>=1&&selections==1&&settingsClicks==1&&mouseDowns>=2;
        check("native display picker commits mode and restores floating window and entry",passed,new{initialInstalled,settings.DisplayMode,savedMode=saved.Settings.DisplayMode,window.IsVisible,installed=Installed(),positionRestored,opens,selections,settingsClicks,mouseDowns,keyDowns});
        File.WriteAllText(Path.Combine(output,"input-result.json"),JsonSerializer.Serialize(new{passed,productDllSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),processId=RuntimeCompat.ProcessId,hwnd=hwnd.ToInt64(),initialInstalled,mode=settings.DisplayMode,savedMode=saved.Settings.DisplayMode,window.IsVisible,installed=Installed(),positionRestored,opens,selections,settingsClicks,mouseDowns,keyDowns,scope="Native UI switch from controlled initially open menu panel to floating, actual Shell registration and file persistence. Not a native click on Explorer's tray icon, not fullscreen/game/hardware acceptance."},AtomicStore.JsonOptions));
        while(watch.Elapsed.TotalSeconds<240&&!File.Exists(Path.Combine(output,"stop-review.txt")))await Task.Delay(100);
    }
}
