using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal sealed class FullscreenFixture : IFullscreenSource
{
    public bool Active { get; set; }
    public bool IsFullscreenActive() => Active;
}

internal static partial class Program
{
    private static async Task RunFullscreenChecks(IslandWindow window, Action<string,bool,object?> check)
    {
        T Field<T>(string name) => (T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name, params object?[] args) => typeof(IslandWindow).GetMethod(name,Flags)!.Invoke(window,args);
        var source=Field<FullscreenFixture>("fullscreenSource");
        Field<DispatcherTimer>("fullscreenTimer").Stop();
        var engine=Field<TrackingEngine>("engine");
        var paused=engine.Data.Settings.Paused;
        var native=new WindowInteropHelper(window).Handle;
        var topmost=window.Topmost;
        try
        {
            var primary=new Rectangle(0,0,1920,1080);var left=new Rectangle(-2560,-200,2560,1440);
            check("fullscreen physical client covers primary",WindowsFullscreenSource.CoversMonitor(primary,primary),null);
            check("fullscreen supports secondary monitor with negative origin",WindowsFullscreenSource.CoversMonitor(left,left),null);
            check("maximized work area is not fullscreen",!WindowsFullscreenSource.CoversMonitor(new(0,30,1920,1010),primary),null);
            check("empty and smaller clients are not fullscreen",!WindowsFullscreenSource.CoversMonitor(Rectangle.Empty,primary)&&!WindowsFullscreenSource.CoversMonitor(new(10,0,1900,1080),primary),null);
            check("one physical pixel rounding is tolerated",WindowsFullscreenSource.CoversMonitor(new(1,1,1918,1078),primary),null);
            check("spanning fullscreen covers any whole monitor",WindowsFullscreenSource.CoversMonitor(new(-2560,-200,4480,1440),primary),null);
            Call("ToggleExpanded",null,new RoutedEventArgs());await Task.Delay(280);
            Call("OpenRecognition",window,new RoutedEventArgs());await Task.Delay(300);
            Field<Button>("AutomaticRecognitionButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(200);
            check("fullscreen setup has open recognition popup",Field<Popup>("recognitionPicker").IsOpen,null);
            source.Active=true;Call("RefreshFullscreenAvoidance");
            check("fullscreen hides complete island immediately",!window.IsVisible&&Field<bool>("fullscreenSuppressed")&&!window.Topmost,null);
            check("fullscreen closes popup and focus return",!Field<Popup>("recognitionPicker").IsOpen&&!Field<DispatcherTimer>("focusReturn").IsEnabled,null);
            var surfaces=Field<object>("surfaces");
            check("fullscreen hides shadow and acrylic surfaces",!(bool)surfaces.GetType().GetProperty("ShadowVisible")!.GetValue(surfaces)!&&!(bool)surfaces.GetType().GetProperty("BackdropVisible")!.GetValue(surfaces)!,null);
            check("fullscreen leaves tracking pause and sampling unchanged",engine.Data.Settings.Paused==paused&&!Field<bool>("suspended"),null);
            Call("ShowIsland");check("tray show cannot bring island over fullscreen",!window.IsVisible,null);
            source.Active=false;Call("RefreshFullscreenAvoidance");
            check("fullscreen exit debounce prevents immediate flash",!window.IsVisible,null);
            await Task.Delay(400);source.Active=true;Call("RefreshFullscreenAvoidance");source.Active=false;Call("RefreshFullscreenAvoidance");
            await Task.Delay(700);Call("RefreshFullscreenAvoidance");check("fullscreen reentry resets restore debounce",!window.IsVisible,null);
            await Task.Delay(400);var foreground=GetForegroundForFullscreenTest();Call("RefreshFullscreenAvoidance");
            check("fullscreen restores island after stable clear",window.IsVisible&&!Field<bool>("fullscreenSuppressed"),null);
            check("automatic restore preserves foreground and HWND",GetForegroundForFullscreenTest()==foreground&&new WindowInteropHelper(window).Handle==native,null);
            Call("HideIsland",null,new RoutedEventArgs());source.Active=true;Call("RefreshFullscreenAvoidance");source.Active=false;Call("RefreshFullscreenAvoidance");await Task.Delay(1100);Call("RefreshFullscreenAvoidance");
            check("manual hidden island stays hidden after game",!window.IsVisible,null);
            Call("ShowIsland");source.Active=true;Call("RefreshFullscreenAvoidance");Call("HideIsland",null,new RoutedEventArgs());source.Active=false;Call("RefreshFullscreenAvoidance");await Task.Delay(1100);Call("RefreshFullscreenAvoidance");
            check("manual hide cancels pending automatic restoration",!window.IsVisible,null);
            Call("ShowIsland");source.Active=true;Call("RefreshFullscreenAvoidance");source.Active=false;
            typeof(IslandWindow).GetField("suspended",Flags)!.SetValue(window,true);Call("RefreshFullscreenAvoidance");await Task.Delay(1100);Call("RefreshFullscreenAvoidance");
            check("session suspension prevents automatic restore",!window.IsVisible,null);
            typeof(IslandWindow).GetField("suspended",Flags)!.SetValue(window,false);Call("RefreshFullscreenAvoidance");await Task.Delay(1100);Call("RefreshFullscreenAvoidance");
            check("unlock can restore after fullscreen clears",window.IsVisible,null);
            var startupSource=new FullscreenFixture{Active=true};
            var folder=System.IO.Path.GetDirectoryName(Field<AtomicStore>("store").Path)!;
            var startupWindow=new IslandWindow(new AtomicStore(System.IO.Path.Combine(folder,"fullscreen-startup-state.json")),new AppState(),false,null,
                startupRegistration:new IsolatedStartupRegistration(System.IO.Path.Combine(folder,"fullscreen-startup-registration.json"),"isolated"),fullscreenSource:startupSource);
            try
            {
                typeof(IslandWindow).GetMethod("PrepareFullscreenStartup",Flags)!.Invoke(startupWindow,null);
                check("startup during fullscreen is transparent and cannot activate",startupWindow.Opacity==0&&!startupWindow.ShowActivated&&!startupWindow.Topmost,null);
                startupWindow.Show();await Task.Delay(100);
                check("startup during fullscreen stays hidden after Loaded",!startupWindow.IsVisible,null);
                typeof(IslandWindow).GetMethod("HideToTray",Flags)!.Invoke(startupWindow,null);
                startupSource.Active=false;await Task.Delay(1500);
                check("start minimized cancels startup fullscreen restoration",!startupWindow.IsVisible,null);
            }
            finally{typeof(IslandWindow).GetField("exiting",Flags)!.SetValue(startupWindow,true);startupWindow.Close();}
        }
        finally
        {
            source.Active=false;typeof(IslandWindow).GetField("suspended",Flags)!.SetValue(window,false);
            typeof(IslandWindow).GetField("fullscreenSuppressed",Flags)!.SetValue(window,false);
            Call("CloseRecognitionPicker");Call("ShowPage","home",false);Call("CollapseToTimer");
            window.ShowActivated=false;window.Topmost=topmost;window.Show();await Task.Delay(300);Field<DispatcherTimer>("focusReturn").Stop();
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="GetForegroundWindow")]
    private static extern nint GetForegroundForFullscreenTest();
}
