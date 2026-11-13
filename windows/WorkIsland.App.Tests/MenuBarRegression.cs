using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunMenuBarChecks(IslandWindow window,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var saved=JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions);
        var folder=Path.GetDirectoryName(Field<AtomicStore>("store").Path)!;
        var source=Field<FullscreenFixture>("fullscreenSource");var topmost=window.Topmost;
        var poll=Field<DispatcherTimer>("fullscreenTimer");poll.Stop();
        var display=Field<ComboBox>("DisplayPicker");
        var native=Field<object>("statusItem");
        bool Installed()=>(bool)native.GetType().GetProperty("Installed")!.GetValue(native)!;
        void Select(string tag)=>display.SelectedItem=display.Items.Cast<ComboBoxItem>().Single(i=>i.Tag?.ToString()==tag);
        try
        {
            check("display picker exposes both real modes",display.Items.Cast<ComboBoxItem>().Select(i=>i.Tag?.ToString()).SequenceEqual(new[]{"floating","menuBar"}),null);
            Call("OpenSettings",window,new RoutedEventArgs());
            var originalPosition=new System.Windows.Point(window.Left,window.Top);
            Select("menuBar");await Task.Delay(120);
            check("menu mode hides floating island and installs native status item",engine.Data.Settings.DisplayMode=="menuBar"&&!window.IsVisible&&Installed(),null);
            check("menu mode removes duplicate floating tray entry",!Field<System.Windows.Forms.NotifyIcon>("tray").Visible,null);
            check("display mode persists through actual AtomicStore",new AtomicStore(Field<AtomicStore>("store").Path).Load().Settings.DisplayMode=="menuBar",null);
            var anchor=native.GetType().GetProperty("Bounds")!.GetValue(native);
            check("native shell returns status item bounds",anchor is Rectangle{Width:>0,Height:>0},anchor);
            Call("OpenMenuPanel");await Task.Delay(150);
            check("menu click opens navigation directly without root animation",window.IsVisible&&Field<bool>("isExpanded")&&Field<FrameworkElement>("NavigationHome").IsVisible&&Field<object?>("rootTransition") is null,null);
            var callback=native.GetType().GetMethod("Message",Flags)!;
            var hwnd=new WindowInteropHelper(window).Handle;
            object?[] closeCallback=[hwnd,0x8000+73,(nint)0,(nint)((0x5749<<16)|0x400),false];
            callback.Invoke(native,closeCallback);
            check("native selection callback toggles existing menu closed",!window.IsVisible&&(bool)closeCallback[4]!,null);
            object?[] keyboardCallback=[hwnd,0x8000+73,(nint)0,(nint)((0x5749<<16)|0x401),false];
            callback.Invoke(native,keyboardCallback);await Task.Delay(100);
            check("native keyboard callback opens menu without cursor coordinates",window.IsVisible&&Field<bool>("menuPanelOpen"),null);
            check("menu opening keeps compact dimensions",Math.Abs(window.Width-344*.82)<1&&Math.Abs(window.Height-92*.82)<1,null);
            Call("SaveImage2x",Path.Combine(folder,"menu-navigation-2x.png"));
            Call("OpenSettings",window,new RoutedEventArgs());await Task.Delay(300);
            check("menu opacity row is absent and panel stays opaque",Field<FrameworkElement>("OpacityRow").Visibility==Visibility.Collapsed&&window.Opacity==1,null);
            Call("SaveImage2x",Path.Combine(folder,"menu-settings-2x.png"));
            var escape=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent};window.RaiseEvent(escape);
            check("Escape closes menu panel and preserves menu mode",!window.IsVisible&&escape.Handled&&engine.Data.Settings.DisplayMode=="menuBar",null);
            Call("OpenMenuPanel");source.Active=true;Call("RefreshFullscreenAvoidance");
            check("fullscreen closes menu panel without requesting later reopen",!window.IsVisible&&!Field<bool>("restoreAfterFullscreen"),null);
            Call("ShowIsland");check("fullscreen blocks menu entry activation",!window.IsVisible,null);
            source.Active=false;Call("RefreshFullscreenAvoidance");await Task.Delay(1100);Call("RefreshFullscreenAvoidance");
            check("game exit cannot reopen transient menu panel",!window.IsVisible&&!Field<bool>("fullscreenSuppressed"),null);
            Call("OpenMenuPanel");Call("OpenSettings",window,new RoutedEventArgs());await Task.Delay(300);Select("floating");await Task.Delay(300);
            check("switching back restores floating location and removes native menu icon",window.IsVisible&&!Installed()&&engine.Data.Settings.DisplayMode=="floating"&&Math.Abs(window.Left-originalPosition.X)<1&&Math.Abs(window.Top-originalPosition.Y)<1,null);
            check("visible floating mode has no duplicate tray and restores opacity setting",!Field<System.Windows.Forms.NotifyIcon>("tray").Visible&&Field<FrameworkElement>("OpacityRow").Visibility==Visibility.Visible,null);
            var work=new Rectangle(-1920,-200,1920,1080);
            var lower=IslandWindow.MenuPanelFrame(new(352,94),new(-100,870,20,20),work);
            var upper=IslandWindow.MenuPanelFrame(new(352,94),new(-1900,-200,20,20),work);
            check("menu geometry stays on negative-origin monitor",work.Contains(lower)&&work.Contains(upper)&&upper.Top>=-172,null);
            using var glyph=new StatusGlyphArtwork();
            foreach(var size in new[]{16,20,24,32})
            {
                using var bitmap=glyph.Render("tracking","green",.5,size);bitmap.Save(Path.Combine(folder,$"menu-time-{size}.png"));
            }
            check("native pixel clock renders supported shell sizes",new[]{16,20,24,32}.All(size=>File.Exists(Path.Combine(folder,$"menu-time-{size}.png"))),null);
            // Real activation outside the app tests dismissal without global hooks.
            Call("ChangeDisplayMode","menuBar");Call("OpenMenuPanel");
            var probe=new Window{Width=120,Height=60,Left=500,Top=350,ShowInTaskbar=false,Title="Work Island menu focus fixture"};
            try
            {
                probe.Show();probe.Activate();await Task.Delay(300);
                check("outside activation dismisses menu panel",probe.IsActive&&!window.IsVisible,null);
            }
            finally{probe.Close();}
            var loadedSettings=new AtomicStore(Field<AtomicStore>("store").Path).Load();loadedSettings.Settings.DisplayMode="menuBar";
            var startup=new IslandWindow(new AtomicStore(Path.Combine(folder,"menu-startup-state.json")),loadedSettings,false,null,
                startupRegistration:new IsolatedStartupRegistration(Path.Combine(folder,"menu-startup-registration.json"),"isolated"),fullscreenSource:new FullscreenFixture());
            object? startupNative=null;
            try
            {
                typeof(IslandWindow).GetMethod("PrepareFullscreenStartup",Flags)!.Invoke(startup,null);
                check("menu startup preflight prevents island flash and activation",startup.Opacity==0&&!startup.ShowActivated,null);
                startup.Show();await Task.Delay(120);
                startupNative=typeof(IslandWindow).GetField("statusItem",Flags)!.GetValue(startup)!;
                check("menu mode survives new native window startup",!startup.IsVisible&&(bool)startupNative.GetType().GetProperty("Installed")!.GetValue(startupNative)!,null);
            }
            finally{typeof(IslandWindow).GetField("exiting",Flags)!.SetValue(startup,true);startup.Close();}
            check("closing menu instance unregisters native shell item",startupNative is not null&&!(bool)startupNative.GetType().GetProperty("Installed")!.GetValue(startupNative)!,null);
        }
        finally
        {
            source.Active=false;
            typeof(IslandWindow).GetField("fullscreenSuppressed",Flags)!.SetValue(window,false);
            var restored=JsonSerializer.Deserialize<AppState>(saved,AtomicStore.JsonOptions)!;engine.Data.Settings=restored.Settings;
            engine.Data.Sessions.Clear();engine.Data.Sessions.AddRange(restored.Sessions);engine.ResetObservation();
            Call("ApplyDisplayMode");Call("ProfileChanged");
            typeof(IslandWindow).GetField("loadingPreferences",Flags)!.SetValue(window,true);display.SelectedIndex=0;
            typeof(IslandWindow).GetField("loadingPreferences",Flags)!.SetValue(window,false);
            window.Topmost=topmost;window.ShowActivated=false;await Task.Delay(300);Field<DispatcherTimer>("focusReturn").Stop();
        }
    }
}
