using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunModeSaveChecks(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var settings=Field<TrackingEngine>("engine").Data.Settings;settings.ReduceMotion=true;settings.Language="zh";
        var store=Field<AtomicStore>("store");var picker=Field<ComboBox>("DisplayPicker");var item=Field<object>("statusItem");
        bool Installed()=>(bool)item.GetType().GetProperty("Installed")!.GetValue(item)!;
        string? Selection()=>((ComboBoxItem?)picker.SelectedItem)?.Tag?.ToString();
        void Select(string mode)=>picker.SelectedItem=picker.Items.Cast<ComboBoxItem>().Single(i=>i.Tag?.ToString()==mode);
        Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenSettings",window,new RoutedEventArgs());await Task.Delay(80);Field<DispatcherTimer>("focusReturn").Stop();
        if(!(bool)Call("Persist")!)throw new InvalidOperationException("Initial save failed");
        var before=File.ReadAllBytes(store.Path);var previousX=settings.WindowX;var previousY=settings.WindowY;var previousLeft=window.Left;var previousTop=window.Top;
        object? failedFloating=null,failedMenu=null;
        using(var locked=new FileStream(store.Path,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            Select("menuBar");await Task.Delay(150);Field<DispatcherTimer>("focusReturn").Stop();
            failedFloating=new{settings.DisplayMode,selection=Selection(),window.IsVisible,installed=Installed(),page=Field<FrameworkElement>("activePage").Name};
            check("failed floating-to-menu save retains floating mode and selector",settings.DisplayMode=="floating"&&Selection()=="floating",failedFloating);
            check("failed floating-to-menu save retains visible settings and normal tray",window.IsVisible&&Field<FrameworkElement>("activePage").Name=="SettingsPage"&&!Installed()&&Field<System.Windows.Forms.NotifyIcon>("tray").Visible,failedFloating);
            check("failed mode change reports visible concise save failure",window.IsVisible&&Field<TextBlock>("MessageLabel").IsVisible&&Field<TextBlock>("MessageLabel").Text.Contains("保存失败"),Field<TextBlock>("MessageLabel").Text);
            check("failed mode change preserves exact primary bytes",File.ReadAllBytes(store.Path).SequenceEqual(before),null);
            check("failed mode change restores saved floating position fields and window geometry",settings.WindowX==previousX&&settings.WindowY==previousY&&window.Left==previousLeft&&window.Top==previousTop,null);
            Call("SaveImage2x",Path.Combine(output,"failed-floating-settings.png"));
        }
        // Restore the old behavior's in-memory mode too, so both directions can
        // be reproduced in the same retained baseline without a second launch.
        Select("floating");Call("ChangeDisplayMode","floating");Call("Persist");
        if(!Field<bool>("isExpanded"))Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenSettings",window,new RoutedEventArgs());
        Select("menuBar");await Task.Delay(100);
        check("successful retry durably switches to real installed menu item",settings.DisplayMode=="menuBar"&&!window.IsVisible&&Installed()&&store.Load().Settings.DisplayMode=="menuBar",null);
        Call("OpenMenuPanel");Call("OpenSettings",window,new RoutedEventArgs());await Task.Delay(100);Field<DispatcherTimer>("focusReturn").Stop();
        var menuBefore=File.ReadAllBytes(store.Path);
        using(var locked=new FileStream(store.Path,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            Select("floating");await Task.Delay(120);Field<DispatcherTimer>("focusReturn").Stop();
            failedMenu=new{settings.DisplayMode,selection=Selection(),window.IsVisible,installed=Installed(),page=Field<FrameworkElement>("activePage").Name};
            check("failed menu-to-floating save retains menu mode and selector",settings.DisplayMode=="menuBar"&&Selection()=="menuBar",failedMenu);
            check("failed menu-to-floating save retains visible settings and menu entry",window.IsVisible&&Field<FrameworkElement>("activePage").Name=="SettingsPage"&&Installed()&&!Field<System.Windows.Forms.NotifyIcon>("tray").Visible,failedMenu);
            check("failed menu-to-floating save preserves saved mode bytes",File.ReadAllBytes(store.Path).SequenceEqual(menuBefore),null);
        }
        // Ensure retry is a real picker change even on the retained baseline,
        // whose failed switch already changed the in-memory mode.
        Select("menuBar");Call("OpenMenuPanel");Call("OpenSettings",window,new RoutedEventArgs());
        Select("floating");await Task.Delay(100);
        check("second successful retry durably restores visible floating mode",settings.DisplayMode=="floating"&&window.IsVisible&&!Installed()&&store.Load().Settings.DisplayMode=="floating",null);
        File.WriteAllText(Path.Combine(output,"mode-save.json"),JsonSerializer.Serialize(new{productDllSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),failedFloating,failedMenu,scope="Actual primary-file lock, ComboBox selection changes, native Shell icon registration and production mode-switch handlers. Menu panel opening is a controlled internal precondition, not a click on Explorer's icon; no user data."},AtomicStore.JsonOptions));
    }
}
