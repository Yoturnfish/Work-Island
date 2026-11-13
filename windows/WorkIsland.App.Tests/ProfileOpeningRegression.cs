using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunProfileOpeningChecks(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        void Set(string name,object value)=>typeof(IslandWindow).GetField(name,Flags)!.SetValue(window,value);
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var source=Field<FullscreenFixture>("fullscreenSource");var engine=Field<TrackingEngine>("engine");
        var store=Field<AtomicStore>("store");var color=Field<Button>("RecognitionColorButton");var menus=new List<ContextMenu>();
        engine.Data.Settings.Paused=true;engine.Data.Settings.Rules=[];engine.Data.Settings.ReduceMotion=true;
        engine.ResetObservation();Field<DispatcherTimer>("fullscreenTimer").Stop();Field<DispatcherTimer>("focusReturn").Stop();
        void OpenPage(){if(!Field<bool>("isExpanded"))Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenRecognition",window,new RoutedEventArgs());window.UpdateLayout();}
        void Track(){if(color.ContextMenu is {} menu&&!menus.Contains(menu))menus.Add(menu);}
        void Close(){Track();foreach(var menu in menus)menu.IsOpen=false;Field<DispatcherTimer>("focusReturn").Stop();}
        void Click(){color.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Track();}
        bool IsOpen()=>color.ContextMenu?.IsOpen==true;
        // A real routed ContextMenuOpening event exercises the owner's XAML
        // subscription. Coordinates and user-initiated input are irrelevant to
        // this guard; this does not pretend to be native mouse/keyboard input.
        ContextMenuEventArgs ContextOpening()
        {
            var args=(ContextMenuEventArgs)RuntimeHelpers.GetUninitializedObject(typeof(ContextMenuEventArgs));
            args.RoutedEvent=ContextMenuService.ContextMenuOpeningEvent;args.Source=color;color.RaiseEvent(args);return args;
        }
        async Task Clear()
        {
            Close();source.Active=false;Set("suspended",false);Set("exiting",false);Set("startupSuppressed",false);
            Call("RefreshFullscreenAvoidance");await Task.Delay(1100);Call("RefreshFullscreenAvoidance");
            if(!window.IsVisible)Call("ShowIsland");OpenPage();await Task.Delay(70);Field<DispatcherTimer>("focusReturn").Stop();
        }
        void Blocked(string label)
        {
            var previous=color.ContextMenu;Click();
            check(label+": late color click does not open a profile popup",!IsOpen(),new{window.IsVisible,suppressed=Field<bool>("fullscreenSuppressed")});
            check(label+": blocked click does not allocate a replacement menu",ReferenceEquals(previous,color.ContextMenu),null);Close();
        }
        try
        {
            OpenPage();await Task.Delay(100);Click();
            check("visible clear color click opens real profile context menu",IsOpen(),null);Close();Call("Persist");
            var bytes=File.ReadAllBytes(store.Path);var state=JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions);
            source.Active=true;Blocked("fullscreen before next poll");
            check("color click detects new fullscreen and hides owner at the boundary",Field<bool>("fullscreenSuppressed")&&!window.IsVisible,null);
            Call("RefreshFullscreenAvoidance");Blocked("already fullscreen suppressed");
            source.Active=false;Call("RefreshFullscreenAvoidance");Blocked("clear debounce");
            await Clear();check("stable clear restores visible floating island",window.IsVisible&&!Field<bool>("fullscreenSuppressed"),null);
            Set("suspended",true);Blocked("session suspended");Set("suspended",false);
            Call("HideIsland",null,new RoutedEventArgs());Blocked("manually hidden");
            check("late color click does not undo manual hide",!window.IsVisible&&!Field<bool>("restoreAfterFullscreen"),null);
            await Clear();Set("exiting",true);Blocked("exit in progress");Set("exiting",false);
            Set("startupSuppressed",true);Blocked("startup suppression");await Clear();
            // The menu is retained on the button after a normal left click, so
            // native context-menu service can reopen it without the Click handler.
            Click();Close();source.Active=true;var opening=ContextOpening();
            check("retained context menu opening is cancelled before next fullscreen poll",opening.Handled,null);
            check("context menu opening also refreshes fullscreen owner suppression",Field<bool>("fullscreenSuppressed")&&!window.IsVisible,null);
            Call("RefreshFullscreenAvoidance");opening=ContextOpening();check("retained menu is cancelled while already fullscreen suppressed",opening.Handled,null);
            await Clear();Set("suspended",true);opening=ContextOpening();check("retained menu is cancelled while suspended",opening.Handled,null);Set("suspended",false);
            Call("HideIsland",null,new RoutedEventArgs());opening=ContextOpening();check("retained menu is cancelled while manually hidden",opening.Handled,null);
            await Clear();Set("exiting",true);opening=ContextOpening();check("retained menu is cancelled while exiting",opening.Handled,null);Set("exiting",false);
            opening=ContextOpening();
            // Complete the native service's default step only when the owner
            // has not handled the request itself. Both implementations must
            // still present a usable menu in the ordinary visible case.
            if(!opening.Handled&&color.ContextMenu is {} retained)retained.IsOpen=true;
            Track();check("visible clear context request presents a usable menu",IsOpen(),null);
            Click();check("normal profile popup still opens after all blocked paths",IsOpen(),null);Close();
            check("opening gates preserve paused rules profiles and raw saved bytes",JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions)==state&&File.ReadAllBytes(store.Path).AsSpan().SequenceEqual(bytes),null);
            File.WriteAllText(Path.Combine(output,"profile-opening.json"),JsonSerializer.Serialize(new{scope="Actual WPF color Button.Click and owner ContextMenuOpening routed events, actual ContextMenu objects; controlled fullscreen source with polling stopped. No physical right click, game, system lock, user settings or credential flow."},AtomicStore.JsonOptions));
        }
        finally
        {
            Track();
            var teardown=await WpfPopupTeardown.CloseAsync(menus,TimeSpan.FromSeconds(3));
            check("profile test teardown observes actual popup closure unload and capture release",teardown.Passed,teardown);
            File.WriteAllText(Path.Combine(output,"profile-teardown.json"),JsonSerializer.Serialize(teardown,AtomicStore.JsonOptions));
            source.Active=false;Set("suspended",false);Set("exiting",false);Set("startupSuppressed",false);Set("fullscreenSuppressed",false);
            window.ShowActivated=false;window.Show();Call("CollapseToTimer");Field<DispatcherTimer>("focusReturn").Stop();
        }
    }
}
