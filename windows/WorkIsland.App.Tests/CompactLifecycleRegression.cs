using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunCompactLifecycleChecks(IslandWindow window,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var saved=JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions);
        var compact=Field<FrameworkElement>("CompactPanel");var progress=Field<PixelProgress>("GoalProgress");var pulse=(ScaleTransform)Field<FrameworkElement>("StatusIcon").RenderTransform;
        void State(string name){engine.Data.Settings.Paused=name=="paused";engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new(name=="tracking"?"lifecycle.exe":"unknown.exe","Lifecycle",0));Call("Render");}
        try
        {
            var initial=new{expanded=Field<bool>("isExpanded"),compact.IsVisible,compact.Opacity,progress.ScanAnimating};
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Field<AtomicStore>("store").Path)!,"compact-lifecycle-start.json"),JsonSerializer.Serialize(initial,AtomicStore.JsonOptions));
            // This case verifies compact -> expanded, regardless of the page
            // left by prior checks or legitimate desktop input during the run.
            Call("CollapseToTimer");await Task.Delay(300);
            engine.Data.Settings.Rules=[new("lifecycle.exe","Lifecycle")];engine.Data.Settings.ReduceMotion=false;State("tracking");await Task.Delay(400);
            check("visible compact progress scans before expansion",compact.IsVisible&&progress.ScanAnimating,initial);
            Call("ToggleExpanded",null!,new RoutedEventArgs());State("tracking");await Task.Delay(70);
            check("outgoing compact progress keeps scanning during the visible root fade",compact.IsVisible&&compact.Opacity>0&&compact.Opacity<1&&progress.ScanAnimating,new{compact.IsVisible,compact.Opacity,progress.ScanAnimating});
            await Task.Delay(400);
            check("settled expanded page stops hidden progress clocks without a timer tick",!compact.IsVisible&&!progress.ScanAnimating&&!progress.PulseAnimating,null);
            State("paused");await Task.Delay(400);
            check("paused status breathing stays stopped while compact panel is hidden",!pulse.HasAnimatedProperties,null);
            State("away");typeof(IslandWindow).GetField("awaySince",Flags)!.SetValue(window,DateTimeOffset.Now.AddSeconds(-12));Call("Render");await Task.Delay(400);
            check("away state cannot restart hidden compact progress or coffee breathing",!progress.PulseAnimating&&!progress.ScanAnimating&&!pulse.HasAnimatedProperties,null);
            Call("CollapseToTimer");State("tracking");await Task.Delay(70);
            check("incoming compact progress resumes during its visible root fade",compact.IsVisible&&compact.Opacity>0&&compact.Opacity<1&&progress.ScanAnimating,null);
            await Task.Delay(400);Call("ToggleExpanded",null!,new RoutedEventArgs());State("tracking");await Task.Delay(60);Call("CollapseToTimer");State("tracking");await Task.Delay(400);
            check("rapid root reversal preserves visible scanning and settles to compact",compact.IsVisible&&compact.Opacity==1&&progress.ScanAnimating,null);
            engine.Data.Settings.ReduceMotion=true;Call("Render");check("reduce motion still stops visible compact loops",!progress.ScanAnimating&&!progress.PulseAnimating&&!pulse.HasAnimatedProperties,null);
            engine.Data.Settings.ReduceMotion=false;State("away");await Task.Delay(400);
            check("visible away coffee resumes its breathing",pulse.HasAnimatedProperties,null);
            Call("ToggleExpanded",null!,new RoutedEventArgs());State("away");await Task.Delay(400);
            check("expanding away state releases the completed hidden coffee loop",!pulse.HasAnimatedProperties,null);
            Call("CollapseToTimer");State("tracking");await Task.Delay(400);
            var host=Field<Grid>("PageHost");var detached=new PixelProgress{Width=100,Height=24};var brush=new SolidColorBrush(Colors.Green);detached.Update(.4,"tracking","matrix",brush,false,10);
            check("progress configured before layout stays static until visible and sized",!detached.ScanAnimating&&!detached.PulseAnimating,null);
            host.Children.Add(detached);window.UpdateLayout();await Task.Delay(80);
            check("attached standalone progress starts its own scan",detached.ScanAnimating,null);
            host.Children.Remove(detached);window.UpdateLayout();await Task.Delay(80);
            var observed=(bool)typeof(PixelProgress).GetField("accentObserved",Flags)!.GetValue(detached)!;
            check("unloaded progress releases clocks and shared accent subscription",!detached.ScanAnimating&&!detached.PulseAnimating&&!observed,new{detached.ScanAnimating,detached.PulseAnimating,observed});
            brush.Color=Colors.Blue;host.Children.Add(detached);window.UpdateLayout();await Task.Delay(80);
            var band=(System.Windows.Shapes.Rectangle)typeof(PixelProgress).GetField("filledBand",Flags)!.GetValue(detached)!;
            check("reloaded progress resumes and picks up accent changed while detached",detached.ScanAnimating&&((LinearGradientBrush)band.Fill).GradientStops[3].Color.B==255,null);
            host.Children.Remove(detached);window.UpdateLayout();await Task.Delay(50);
        }
        finally
        {
            var restored=JsonSerializer.Deserialize<AppState>(saved,AtomicStore.JsonOptions)!;engine.Data.Settings=restored.Settings;engine.Data.Sessions.Clear();engine.Data.Sessions.AddRange(restored.Sessions);engine.ResetObservation();Call("CollapseToTimer");Call("ProfileChanged");await Task.Delay(300);
        }
    }
}
