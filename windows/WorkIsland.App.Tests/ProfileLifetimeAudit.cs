using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private sealed class ProfileLifetimeProbe
    {
        public required WeakReference<ContextMenu> Menu;
        public required WeakReference<PixelProgress>[] Previews;
        public int Unloaded;
        public int Closed;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object ProfileLifetimeSnapshot(ProfileLifetimeProbe probe)
    {
        var exists=probe.Menu.TryGetTarget(out var menu);
        var rows=probe.Previews.Select(reference=>
        {
            if(!reference.TryGetTarget(out var preview))return new{alive=false,loaded=false,visible=false,scan=false,pulse=false,accentObserved=false,scanClock=false,pulseClock=false};
            object? Field(string name)=>typeof(PixelProgress).GetField(name,Flags)!.GetValue(preview);
            return new{alive=true,loaded=preview.IsLoaded,visible=preview.IsVisible,scan=preview.ScanAnimating,pulse=preview.PulseAnimating,accentObserved=(bool)Field("accentObserved")!,scanClock=Field("scanClock") is not null,pulseClock=Field("pulseClock") is not null};
        }).ToArray();
        return new{menuAlive=exists,menuOpen=exists&&menu!.IsOpen,probe.Unloaded,probe.Closed,rows};
    }
    private static async Task RunProfileLifetime(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;settings.Paused=true;settings.Rules=[];settings.ReduceMotion=false;
        engine.ResetObservation();Field<DispatcherTimer>("fullscreenTimer").Stop();Field<DispatcherTimer>("focusReturn").Stop();Call("Render");
        Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenRecognition",window,new RoutedEventArgs());await Task.Delay(250);
        var color=Field<Button>("RecognitionColorButton");var probes=new List<ProfileLifetimeProbe>();var snapshots=new List<object>();
        var originalHistory=JsonSerializer.Serialize(engine.Data.Sessions,AtomicStore.JsonOptions);
        var allInitiallyLoaded=true;var allPreviewPolicy=true;
        async Task Cycle(int round,bool hide)
        {
            Call("OpenProfilePopover");var menu=color.ContextMenu!;
            var panel=(StackPanel)((MenuItem)menu.Items[0]).Header;
            var styles=panel.Children.OfType<StackPanel>().Single();
            var previews=styles.Children.OfType<Button>().Select(b=>((Grid)b.Content).Children.OfType<PixelProgress>().Single()).ToArray();
            var probe=new ProfileLifetimeProbe{Menu=new(menu),Previews=previews.Select(p=>new WeakReference<PixelProgress>(p)).ToArray()};probes.Add(probe);
            foreach(var preview in previews)preview.Unloaded+=(_,_)=>probe.Unloaded++;
            menu.Closed+=(_,_)=>probe.Closed++;
            await Task.Delay(100);allInitiallyLoaded&=menu.IsOpen&&previews.All(p=>p.IsLoaded&&p.IsVisible);
            var motionAllowed=!settings.ReduceMotion&&SystemParameters.ClientAreaAnimation;
            allPreviewPolicy&=previews.All(p=>p.ScanAnimating==motionAllowed&&!p.PulseAnimating);
            // Exercise real production color and style handlers while loaded.
            var palette=panel.Children.OfType<UniformGrid>().Single();
            palette.Children.OfType<StackPanel>().ElementAt(round%7).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            styles.Children.OfType<Button>().ElementAt(round%2).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(hide)Call("HideIsland",null,new RoutedEventArgs());else menu.IsOpen=false;
            await Task.Delay(220);
            check($"profile close {round}: both previews unload and release their clocks and brush observations",previews.All(p=>!p.IsLoaded&&!p.ScanAnimating&&!p.PulseAnimating&&!(bool)typeof(PixelProgress).GetField("accentObserved",Flags)!.GetValue(p)!),ProfileLifetimeSnapshot(probe));
            check($"profile close {round}: menu closes exactly once",!menu.IsOpen&&probe.Closed==1&&probe.Unloaded==2,new{probe.Closed,probe.Unloaded});
            if(hide)
            {
                Call("ShowIsland");Call("OpenRecognition",window,new RoutedEventArgs());await Task.Delay(100);
                check($"profile restore {round}: restoring the owner does not reopen its previous color form",!menu.IsOpen,ProfileLifetimeSnapshot(probe));
            }
            snapshots.Add(new{round,hide,after=ProfileLifetimeSnapshot(probe)});
        }
        async Task Collect()
        {
            await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();await Task.Delay(100);
        }
        object Resources()
        {
            using var process=Process.GetCurrentProcess();process.Refresh();
            return new{managedBytes=GC.GetTotalMemory(false),privateBytes=process.PrivateMemorySize64,handles=process.HandleCount,threads=process.Threads.Count};
        }
        var before=Resources();
        try
        {
            for(var i=0;i<12;i++)await Cycle(i,i is 5 or 11);
            Call("ChangeDisplayMode","menuBar");Call("OpenMenuPanel");Call("OpenRecognition",window,new RoutedEventArgs());
            await Task.Delay(120);Field<DispatcherTimer>("focusReturn").Stop();
            await Cycle(12,true);
            check("profile previews are actually loaded and visible during every open",allInitiallyLoaded,null);
            check("profile previews respect the current motion policy while visible",allPreviewPolicy,new{settings.ReduceMotion,SystemParameters.ClientAreaAnimation});
            await Collect();var after=Resources();
            var aliveOld=probes.Take(probes.Count-1).Count(p=>p.Menu.TryGetTarget(out _));
            var aliveOldPreviews=probes.Take(probes.Count-1).SelectMany(p=>p.Previews).Count(p=>p.TryGetTarget(out _));
            check("replaced profile menus are collectible while the application remains open",aliveOld==0,new{aliveOld});
            check("replaced profile previews are collectible while the application remains open",aliveOldPreviews==0,new{aliveOldPreviews});
            check("profile resource workflow preserves history and pause",settings.Paused&&engine.Current.State=="paused"&&JsonSerializer.Serialize(engine.Data.Sessions,AtomicStore.JsonOptions)==originalHistory,null);
            File.WriteAllText(Path.Combine(output,"profile-lifetime.json"),JsonSerializer.Serialize(new{rounds=13,before,after,aliveOld,aliveOldPreviews,snapshots,final=probes.Select(ProfileLifetimeSnapshot).ToArray(),scope="Actual WPF popup lifecycle and production routed controls, private paused fixture. Twelve floating rounds and one menu-panel hide/restore. Explicit GC only inside validator. First 12 superseded menus should collect; latest closed menu may stay referenced by its owner. Current visible-motion policy and close-time clock release checked. No official Computer Use session, physical input, user preview edits, long-run or global CPU-root-cause claim."},AtomicStore.JsonOptions));
        }
        finally
        {
            if(color.ContextMenu is {} menu)menu.IsOpen=false;
            Call("ChangeDisplayMode","floating");
            await Task.Delay(300);Call("ShowPage","home",false);Call("CollapseToTimer");Field<DispatcherTimer>("focusReturn").Stop();
        }
    }
}
