using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunBehaviorMotionChecks(IslandWindow window,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var saved=JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions);
        var clock=Field<FrameworkElement>("ClockIcon");var coffee=Field<FrameworkElement>("CoffeeIcon");var pause=Field<FrameworkElement>("PauseIcon");
        var scale=(ScaleTransform)Field<FrameworkElement>("StatusIcon").RenderTransform;
        void State(string state)
        {
            engine.Data.Settings.Paused=state=="paused";
            engine.Observe(DateTimeOffset.Now,new(state=="tracking"?"behavior-test.exe":"unknown.exe","Test",0));Call("Render");
        }
        double Alpha(FrameworkElement icon)=>icon.Visibility==Visibility.Visible?icon.Opacity:0;
        try
        {
            engine.Data.Settings.Rules=[new("behavior-test.exe","Test")];engine.Data.Settings.ReduceMotion=true;State("tracking");
            engine.Data.Settings.ReduceMotion=false;State("away");await Task.Delay(90);
            check("clock and coffee share a real crossfade",Alpha(clock)>0&&Alpha(clock)<1&&Alpha(coffee)>0&&Alpha(coffee)<1&&Math.Abs(Alpha(clock)+Alpha(coffee)-1)<.02,new{clock=Alpha(clock),coffee=Alpha(coffee)});
            Call("SaveImage2x",Path.Combine(Path.GetDirectoryName(Field<AtomicStore>("store").Path)!,"state-crossfade.png"));
            var before=new[]{Alpha(clock),Alpha(coffee)};State("tracking");
            check("icon reversal retains presented alpha",Math.Abs(Alpha(clock)-before[0])<.04&&Math.Abs(Alpha(coffee)-before[1])<.04,new{before,after=new[]{Alpha(clock),Alpha(coffee)}});
            await Task.Delay(450);
            check("icon clocks release after settling",Alpha(clock)==1&&Alpha(coffee)==0&&!clock.HasAnimatedProperties&&!coffee.HasAnimatedProperties,null);
            State("away");await Task.Delay(450);State("paused");await Task.Delay(90);
            check("pause joins the same icon crossfade",Alpha(pause)>0&&Alpha(pause)<1&&Alpha(coffee)>0&&Math.Abs(Alpha(pause)+Alpha(coffee)-1)<.02,null);
            engine.Data.Settings.ReduceMotion=true;Call("Render");
            check("reduce motion settles current state without another state change",Alpha(pause)==1&&Alpha(coffee)==0&&!pause.HasAnimatedProperties&&!scale.HasAnimatedProperties,null);
            engine.Data.Settings.ReduceMotion=false;State("away");await Task.Delay(700);var presented=scale.ScaleX;State("tracking");
            check("breathing scale does not snap when tracking resumes",Math.Abs(scale.ScaleX-presented)<.005,new{presented,after=scale.ScaleX});
            await Task.Delay(450);check("tracking scale returns to neutral",Math.Abs(scale.ScaleX-1)<.001&&!scale.HasAnimatedProperties,null);
            Call("CollapseToTimer");await Task.Delay(300);Call("ToggleExpanded",null,new RoutedEventArgs());await Task.Delay(60);
            engine.Data.Settings.ReduceMotion=true;Call("Render");
            check("reduced motion settles an in-flight navigation immediately",Field<FrameworkElement>("DetailsPanel").Visibility==Visibility.Visible&&!Field<FrameworkElement>("DetailsPanel").HasAnimatedProperties&&Field<FrameworkElement>("CompactPanel").Visibility==Visibility.Collapsed,null);
            engine.Data.Settings.ReduceMotion=false;Call("OpenRecognition",window,new RoutedEventArgs());await Task.Delay(300);
            Field<Button>("AutomaticRecognitionButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(250);
            var picker=Field<Popup>("recognitionPicker");
            var escape=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent};
            window.RaiseEvent(escape);
            check("Escape closes recognition picker even with owner focus",!picker.IsOpen&&escape.Handled,null);
            Call("CloseRecognitionPicker");
            Field<Button>("AutomaticRecognitionButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(250);
            picker=Field<Popup>("recognitionPicker");
            check("recognition popup takes keyboard focus",picker.IsOpen&&picker.Child.IsKeyboardFocusWithin,null);
            check("recognition popup uses restrained fade and dark scrollbar",picker.PopupAnimation==PopupAnimation.Fade&&picker.Resources[typeof(ScrollBar)]==window.FindResource(typeof(ScrollBar)),null);
            Call("CloseRecognitionPicker");
            var source=Field<RecognitionFixture>("recognitionSource");var calls=source.ApplicationCalls;
            source.DelayMilliseconds=1750;
            try
            {
                Field<Button>("AutomaticRecognitionButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);
                Call("CloseRecognitionPicker");Field<Button>("AutomaticRecognitionButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(150);
                check("repeated picker opens do not queue duplicate stalled scans",source.ApplicationCalls==calls+1,null);
                Call("ProfileChanged");await Task.Delay(1800);
                check("late scan cannot reopen picker after profile change",!Field<Popup>("recognitionPicker").IsOpen,null);
            }
            finally{source.DelayMilliseconds=0;Call("CloseRecognitionPicker");}
            // ContextMenu/Popup roots can steal activation. Test the real keyboard
            // and routed control behavior, not just the handler's boolean flags.
            var day=LogicalDay.Key(DateTimeOffset.Now,engine.Zone,engine.Data.Settings.DayResetMinutes);
            engine.Data.Sessions.Clear();
            engine.Data.Sessions.Add(new(){ApplicationIdentifier="edit-test.exe",ApplicationName="Edit test",DayKey=day,StartedAt=DateTimeOffset.Now.AddHours(-1),EndedAt=DateTimeOffset.Now,EffectiveSeconds=3600});
            Call("ShowHistoryDetail",day);window.UpdateLayout();
            var rows=Field<StackPanel>("HistoryRows");var row=rows.Children.OfType<Grid>().First(g=>g.Children.OfType<StackPanel>().Any());var input=row.Children.OfType<StackPanel>().Single();var hours=(TextBox)input.Children[0];var minutes=(TextBox)input.Children[2];
            engine.Data.Sessions.Add(new(){ApplicationIdentifier="edit-test.exe",ApplicationName="Edit test",DayKey=day,StartedAt=DateTimeOffset.Now,EndedAt=DateTimeOffset.Now,EffectiveSeconds=600});
            hours.Text="2";minutes.Text="0";hours.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Enter){RoutedEvent=Keyboard.KeyDownEvent});
            check("history edit includes records added after page opened",engine.Data.Sessions.Where(s=>s.ApplicationIdentifier=="edit-test.exe").Sum(s=>s.EffectiveSeconds)==7200,null);
        }
        finally
        {
            Call("CloseRecognitionPicker");var restored=JsonSerializer.Deserialize<AppState>(saved,AtomicStore.JsonOptions)!;
            engine.Data.Settings=restored.Settings;engine.Data.Sessions.Clear();engine.Data.Sessions.AddRange(restored.Sessions);engine.ResetObservation();
            Call("ProfileChanged");Call("ShowPage","home",false);Call("CollapseToTimer");await Task.Delay(400);Field<DispatcherTimer>("focusReturn").Stop();
        }
    }
}
