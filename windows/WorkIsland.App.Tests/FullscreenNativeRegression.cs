using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;
using Forms=System.Windows.Forms;

internal static partial class Program
{
    private static int RunFullscreenNativeProbe(string folder)
    {
        var app=new Application();
        var window=new Window{Title="Work Island fullscreen test fixture",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,
            ShowActivated=false,ShowInTaskbar=false,AllowsTransparency=true,Opacity=.12,Background=Brushes.Black,Width=320,Height=160};
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};var started=Stopwatch.StartNew();string previous="";
        timer.Tick+=(_,_)=>
        {
            var command=File.Exists(Path.Combine(folder,"probe-command.txt"))?File.ReadAllText(Path.Combine(folder,"probe-command.txt")):"normal";
            if(command=="exit"||started.Elapsed.TotalSeconds>25){window.Close();return;}
            if(command==previous)return;previous=command;
            window.WindowState=command=="minimized"?WindowState.Minimized:WindowState.Normal;
            if(command!="minimized")
            {
                var screen=Forms.Screen.PrimaryScreen!.Bounds;
                var handle=new WindowInteropHelper(window).Handle;
                var style=GetFullscreenProbeStyle(handle,-20)|0x08000000;
                SetFullscreenProbeStyle(handle,-20,command=="overlay"?style|0x20:style&~0x20);
                var full=command is "fullscreen" or "overlay";
                SetFullscreenProbePosition(new WindowInteropHelper(window).Handle,0,screen.Left,screen.Top,full?screen.Width:320,full?screen.Height:160,0x0014);
            }
            File.WriteAllText(Path.Combine(folder,"probe-ready.txt"),command);
        };
        window.Loaded+=(_,_)=>timer.Start();window.Closed+=(_,_)=>timer.Stop();app.Run(window);return 0;
    }

    private static async Task RunFullscreenNativeChecks(string output,Action<string,bool,object?> check)
    {
        var folder=Path.Combine(output,"native-fullscreen-fixture");Directory.CreateDirectory(folder);
        var command=Path.Combine(folder,"probe-command.txt");var ready=Path.Combine(folder,"probe-ready.txt");
        File.WriteAllText(command,"normal");
        var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
        if(string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath),"dotnet",StringComparison.OrdinalIgnoreCase))info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        info.ArgumentList.Add("--fullscreen-native-probe");info.ArgumentList.Add(folder);
        using var process=Process.Start(info)!;
        async Task<bool> Mode(string value)
        {
            File.WriteAllText(command,value);var clock=Stopwatch.StartNew();
            while(clock.Elapsed.TotalSeconds<5)
            {
                if(File.Exists(ready)&&File.ReadAllText(ready)==value){await Task.Delay(350);return true;}
                await Task.Delay(100);
            }
            return false;
        }
        var source=new WindowsFullscreenSource();
        IslandWindow? island=null;
        try
        {
            check("native ordinary borderless fixture does not suppress",await Mode("normal")&&!source.IsFullscreenActive(),null);
            check("native separate-process passive fullscreen overlay does not suppress",await Mode("overlay")&&!source.IsFullscreenActive(),null);
            island=new IslandWindow(new AtomicStore(Path.Combine(folder,"native-island-state.json")),new AppState(),false,null,
                startupRegistration:new IsolatedStartupRegistration(Path.Combine(folder,"native-registration.json"),"isolated"));
            island.ShowActivated=false;island.Show();await Task.Delay(300);
            check("native passive overlay leaves production island visible",island.IsVisible,null);
            check("native separate-process borderless fullscreen is detected",await Mode("fullscreen")&&source.IsFullscreenActive(),null);
            await Task.Delay(400);
            check("native fullscreen polling hides production island",!island.IsVisible,null);
            var surfaces=typeof(IslandWindow).GetField("surfaces",Flags)!.GetValue(island)!;
            check("native fullscreen polling hides production shadow and acrylic",!(bool)surfaces.GetType().GetProperty("ShadowVisible")!.GetValue(surfaces)!&&!(bool)surfaces.GetType().GetProperty("BackdropVisible")!.GetValue(surfaces)!,null);
            check("native minimized fullscreen stops suppression",await Mode("minimized")&&!source.IsFullscreenActive(),null);
            var foreground=GetForegroundForFullscreenTest();await Task.Delay(1500);
            check("native polling restores island without taking focus",island.IsVisible&&GetForegroundForFullscreenTest()==foreground,null);
            check("native fullscreen switched to passive overlay clears suppression",await Mode("overlay")&&!source.IsFullscreenActive(),null);
            check("native restored ordinary fixture stops suppression",await Mode("normal")&&!source.IsFullscreenActive(),null);
        }
        finally
        {
            if(island is not null){typeof(IslandWindow).GetField("exiting",Flags)!.SetValue(island,true);island.Close();}
            File.WriteAllText(command,"exit");
            await process.WaitForExitAsync();
            check("native fullscreen fixture exits normally",process.ExitCode==0,null);
        }
    }

    [DllImport("user32.dll",EntryPoint="SetWindowPos")]
    private static extern bool SetFullscreenProbePosition(nint window,nint after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]private static extern nint GetFullscreenProbeStyle(nint window,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]private static extern nint SetFullscreenProbeStyle(nint window,int index,nint value);
}
