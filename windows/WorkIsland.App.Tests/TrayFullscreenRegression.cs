using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;
using Forms=System.Windows.Forms;

internal static partial class Program
{
    private static async Task RunTrayFullscreenChecks(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var source=Field<FullscreenFixture>("fullscreenSource");var settings=Field<TrackingEngine>("engine").Data.Settings;settings.Paused=true;
        Field<DispatcherTimer>("fullscreenTimer").Stop();var menu=Field<Forms.NotifyIcon>("tray").ContextMenuStrip!;
        var where=window.PointToScreen(new Point(30,30));var position=new System.Drawing.Point((int)where.X,(int)where.Y);
        void Open()=>menu.Show(position);
        async Task Clear(){source.Active=false;Call("RefreshFullscreenAvoidance");await Task.Delay(1100);Call("RefreshFullscreenAvoidance");}
        try
        {
            Open();check("ordinary floating tray context menu still opens",menu.Visible,null);menu.Close();
            source.Active=true;Open();
            check("context opening detects fullscreen before next poll",Field<bool>("fullscreenSuppressed")&&!window.IsVisible,null);
            check("first fullscreen tray request cannot expose native menu",!menu.Visible,null);menu.Close();
            Call("RefreshFullscreenAvoidance");Open();
            check("repeated request while already suppressed remains blocked",!menu.Visible,null);menu.Close();
            source.Active=false;Call("RefreshFullscreenAvoidance");Open();
            check("clear debounce also blocks tray context menu",!menu.Visible,null);menu.Close();
            var foreground=GetForegroundForFullscreenTest();await Clear();
            check("stable clear restores island without taking foreground",window.IsVisible&&!Field<bool>("fullscreenSuppressed")&&GetForegroundForFullscreenTest()==foreground,null);
            Open();check("tray context menu works again after stable clear",menu.Visible,null);menu.Close();
            Call("HideIsland",null,new RoutedEventArgs());source.Active=true;Open();
            check("blocked tray request preserves manual hide intent",!menu.Visible&&!window.IsVisible&&!Field<bool>("restoreAfterFullscreen"),null);menu.Close();
            await Clear();check("blocked context request does not schedule a hidden island restore",!window.IsVisible,null);
            Call("ShowIsland");typeof(IslandWindow).GetField("suspended",Flags)!.SetValue(window,true);Open();
            check("session suspension blocks context opening",!menu.Visible,null);menu.Close();typeof(IslandWindow).GetField("suspended",Flags)!.SetValue(window,false);
            Call("ChangeDisplayMode","menuBar");source.Active=true;
            var status=Field<object>("statusItem");var hwnd=new WindowInteropHelper(window).Handle;
            object?[] callback=[hwnd,0x8000+73,(nint)0,(nint)((0x5749<<16)|0x7b),false];status.GetType().GetMethod("Message",Flags)!.Invoke(status,callback);
            check("native menu-mode context callback reaches guarded opening",(bool)callback[4]!&&!menu.Visible&&Field<bool>("fullscreenSuppressed"),null);menu.Close();
            await Clear();check("fullscreen exit leaves transient menu panel closed",!window.IsVisible&&!Field<bool>("menuPanelOpen"),null);
            Open();check("menu-mode context menu works after stable clear",menu.Visible,null);menu.Close();
            check("context suppression leaves manual pause unchanged",settings.Paused,null);
            File.WriteAllText(Path.Combine(output,"tray-fullscreen-scope.json"),"{\"scope\":\"Actual native Forms ContextMenuStrip.Show and native status callback with controlled FullscreenFixture; no real game, system suspension or physical tray input acceptance.\"}");
        }
        finally
        {
            menu.Close();source.Active=false;typeof(IslandWindow).GetField("suspended",Flags)!.SetValue(window,false);typeof(IslandWindow).GetField("fullscreenSuppressed",Flags)!.SetValue(window,false);
            Call("ChangeDisplayMode","floating");Call("CloseRecognitionPicker");Call("CollapseToTimer");window.ShowActivated=false;window.Show();Field<DispatcherTimer>("focusReturn").Stop();await Task.Delay(250);
        }
    }
}
