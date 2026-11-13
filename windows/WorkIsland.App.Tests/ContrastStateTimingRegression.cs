using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunContrastStateTiming(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Optional(string name)=>typeof(IslandWindow).GetField(name,Flags)?.GetValue(window);
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;
        settings.Paused=false;settings.ReduceMotion=false;settings.Rules=[new("timing.exe","Timing fixture")];
        settings.BackgroundStyle="glass";Call("ApplyMaterial");engine.ResetObservation();
        void State(bool tracking){engine.Observe(DateTimeOffset.Now,new(tracking?"timing.exe":"other.exe","Timing fixture",0));typeof(IslandWindow).GetField("lastContrastSample",Flags)!.SetValue(window,Stopwatch.GetTimestamp());Call("Render");}
        var policy=Field<object>("textContrast");var observe=policy.GetType().GetMethod("Observe",Flags|BindingFlags.Public)!;
        void Contrast(double value){observe.Invoke(policy,[value]);observe.Invoke(policy,[value]);typeof(IslandWindow).GetField("lastContrastSample",Flags)!.SetValue(window,Stopwatch.GetTimestamp());Call("Render");}
        State(true);Contrast(.01);await Task.Delay(450);
        var coffee=Field<FrameworkElement>("CoffeeIcon");var clock=Field<FrameworkElement>("ClockIcon");
        check("timing fixture starts on stable tracking",clock.Opacity==1&&coffee.Opacity==0,null);
        var samples=new List<object>();var watch=Stopwatch.StartNew();State(false);
        var stateClock=Optional("stateTransition");
        await Task.Delay(170);var before=coffee.Opacity;Contrast(.95);var after=coffee.Opacity;
        check("background-only change preserves the running icon clock identity",stateClock is not null&&ReferenceEquals(stateClock,Optional("stateTransition")),new{ms=watch.Elapsed.TotalMilliseconds,before,after});
        check("background-only change preserves current icon opacity",Math.Abs(before-after)<.08,new{before,after});
        bool? atStateDeadline=null;
        while(watch.Elapsed.TotalMilliseconds<620)
        {
            var ms=watch.Elapsed.TotalMilliseconds;
            samples.Add(new{ms,coffee=coffee.Opacity,clock=clock.Opacity,title=Field<SolidColorBrush>("compactTitleInk").Color.R,stateClock=Optional("stateTransition") is not null,inkClock=Optional("compactInkMotion") is not null});
            if(atStateDeadline is null&&ms>=380)atStateDeadline=coffee.Opacity>=.999&&Optional("stateTransition") is null;
            await Task.Delay(16);
        }
        check("icon transition still finishes at the original 350ms deadline",atStateDeadline==true,new{coffee=coffee.Opacity,atStateDeadline});
        check("independent ink transition also settles and releases its clock",coffee.Opacity==1&&Field<SolidColorBrush>("compactTitleInk").Color.R==32&&Optional("compactInkMotion") is null,null);
        Contrast(.01);await Task.Delay(70);var colorBefore=Field<SolidColorBrush>("compactTitleInk").Color;
        Contrast(.95);var colorAfter=Field<SolidColorBrush>("compactTitleInk").Color;
        check("contrast-only reversal cannot start an icon clock",Optional("stateTransition") is null,new{colorBefore,colorAfter});
        check("contrast-only reversal starts at presented ink",Math.Abs(colorBefore.R-colorAfter.R)<12,new{colorBefore,colorAfter});
        await Task.Delay(450);
        State(true);await Task.Delay(100);State(false);var reverse=Optional("stateTransition");await Task.Delay(100);Contrast(.01);
        check("contrast during rapid state reversal preserves its new deadline",ReferenceEquals(reverse,Optional("stateTransition")),null);
        await Task.Delay(300);check("rapid state reversal reaches its original endpoint",coffee.Opacity==1&&clock.Opacity==0&&Optional("stateTransition") is null,null);
        await Task.Delay(200);settings.ReduceMotion=true;Contrast(.95);
        check("reduced motion settles icon and ink clocks together",Optional("stateTransition") is null&&Optional("compactInkMotion") is null&&Field<SolidColorBrush>("compactTitleInk").Color.R==32,null);
        settings.ReduceMotion=false;State(true);Contrast(.01);await Task.Delay(450);Call("EnableNativeForeground");Call("EnableNativePulses");await Task.Delay(100);
        check("native timing fixture becomes active after settling",Field<bool>("nativeForegroundActive"),null);
        var nativeWatch=Stopwatch.StartNew();State(false);await Task.Delay(170);Contrast(.95);await Task.Delay(220);
        check("finished icon clock cannot freeze still-changing ink into a native texture",Optional("stateTransition") is null&&Optional("compactInkMotion") is not null&&!Field<bool>("nativeForegroundActive"),new{ms=nativeWatch.Elapsed.TotalMilliseconds});
        await Task.Delay(220);check("native foreground returns only after both transitions complete",Field<bool>("nativeForegroundActive")&&Optional("compactInkMotion") is null&&Field<SolidColorBrush>("compactTitleInk").Color.R==32,null);
        Contrast(.01);await Task.Delay(60);window.Hide();
        check("hiding immediately settles and releases the independent ink clock",Optional("compactInkMotion") is null&&!Field<SolidColorBrush>("compactTitleInk").HasAnimatedProperties,null);
        window.Show();Call("Render");await Task.Delay(450);check("show restores the native foreground after an interrupted ink fade",Field<bool>("nativeForegroundActive"),null);
        var closeFolder=Path.Combine(output,"close-fixture");Directory.CreateDirectory(closeFolder);
        var closeWindow=new IslandWindow(new AtomicStore(Path.Combine(closeFolder,"state.json")),new AppState{Settings=new(){Rules=[],Paused=true}},false,null,
            startupRegistration:new IsolatedStartupRegistration(Path.Combine(closeFolder,"startup.json"),"isolated"),fullscreenSource:new FullscreenFixture());
        closeWindow.ShowActivated=false;closeWindow.Topmost=false;closeWindow.Loaded+=(_,_)=>((DispatcherTimer)typeof(IslandWindow).GetField("timer",Flags)!.GetValue(closeWindow)!).Stop();closeWindow.Show();await Task.Delay(80);
        object? CloseField(string name)=>typeof(IslandWindow).GetField(name,Flags)?.GetValue(closeWindow);
        try
        {
            ((TrackingEngine)CloseField("engine")!).Data.Settings.BackgroundStyle="glass";
            typeof(IslandWindow).GetMethod("ApplyMaterial",Flags|BindingFlags.DeclaredOnly)!.Invoke(closeWindow,null);
            typeof(IslandWindow).GetMethod("Render",Flags|BindingFlags.DeclaredOnly)!.Invoke(closeWindow,null);await Task.Delay(450);
            var closePolicy=CloseField("textContrast")!;observe.Invoke(closePolicy,[.95]);observe.Invoke(closePolicy,[.95]);
            typeof(IslandWindow).GetMethod("Render",Flags|BindingFlags.DeclaredOnly)!.Invoke(closeWindow,null);await Task.Delay(60);
            check("close fixture starts an actual ink color animation",((SolidColorBrush)CloseField("compactTitleInk")!).HasAnimatedProperties,null);
        }
        finally{typeof(IslandWindow).GetField("exiting",Flags)!.SetValue(closeWindow,true);closeWindow.Close();}
        check("closing removes all owned ink animation references",CloseField("compactInkMotion") is null&&!((SolidColorBrush)CloseField("compactTitleInk")!).HasAnimatedProperties,null);
        File.WriteAllText(Path.Combine(output,"contrast-state-timing.json"),JsonSerializer.Serialize(new{samples,scope="Controlled actual WPF clocks and wall-clock properties under overlap of input-state and background contrast targets. Direct model preparation, not native input, physical frames, or a real game."},AtomicStore.JsonOptions));
    }
}
