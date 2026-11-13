using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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
    private static async Task RunProfileStyleFocus(IslandWindow window,string output,Action<string,bool,object?> check,bool live)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;var store=Field<AtomicStore>("store");
        settings.Paused=true;settings.Rules=[];settings.Language="zh";settings.ReduceMotion=true;settings.BackgroundStyle="black";
        engine.ResetObservation();Field<DispatcherTimer>("fullscreenTimer").Stop();Field<DispatcherTimer>("focusReturn").Stop();
        window.Title="Work Island · 样式键盘验收 · "+Environment.ProcessId;window.Left=460;window.Top=300;
        if(live){window.Topmost=true;window.Activate();}
        Call("Persist");Call("Render");
        Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenRecognition",window,new RoutedEventArgs());await Task.Delay(180);Call("OpenProfilePopover");
        Field<DispatcherTimer>("focusReturn").Stop();
        var menu=Field<Button>("RecognitionColorButton").ContextMenu!;
        var panel=(StackPanel)((MenuItem)menu.Items[0]).Header;var styles=panel.Children.OfType<StackPanel>().Single();
        var name=ProfileNameEditor(panel);var matrix=styles.Children.OfType<Button>().First();var wave=styles.Children.OfType<Button>().Last();
        var initialHistory=JsonSerializer.Serialize(engine.Data.Sessions,AtomicStore.JsonOptions);
        var trace=new List<object>();var observed=new TaskCompletionSource();var keySeen=false;
        object Focus()=>new{type=Keyboard.FocusedElement?.GetType().Name,wave=ReferenceEquals(Keyboard.FocusedElement,wave),matrix=ReferenceEquals(Keyboard.FocusedElement,matrix),name=ReferenceEquals(Keyboard.FocusedElement,name),menuOpen=menu.IsOpen,style=settings.ProgressStyle};
        void Write(string file,object value)=>File.WriteAllText(Path.Combine(output,file),JsonSerializer.Serialize(value,AtomicStore.JsonOptions));
        KeyEventHandler preview=(_,e)=>
        {
            trace.Add(new{phase="preview-down",key=e.Key.ToString(),e.Handled,focus=Focus()});
            if(e.Key!=Key.Enter||!ReferenceEquals(Keyboard.FocusedElement,wave)||keySeen)return;
            keySeen=true;
            window.Dispatcher.InvokeAsync(async()=>
            {
                try
                {
                    await Task.Delay(450);
                    check("actual Return selects and saves waveform",settings.ProgressStyle=="waveform"&&store.Load().Settings.ProgressStyle=="waveform",null);
                    check("actual Return retains the profile popup",menu.IsOpen,Focus());
                    check("actual Return leaves the selected style keyboard focused",ReferenceEquals(Keyboard.FocusedElement,wave),Focus());
                    check("actual selected style remains attached to its popup",styles.Children.Contains(wave)&&PresentationSource.FromVisual(wave) is not null,null);
                    Write("native-observation.json",new{keySeen,focus=Focus(),trace});observed.TrySetResult();
                }
                catch(Exception error){observed.TrySetException(error);}
            });
        };
        KeyEventHandler up=(_,e)=>
        {
            trace.Add(new{phase="key-up",key=e.Key.ToString(),e.Handled,focus=Focus()});
            if(live)Write("native-focus-progress.json",new{keySeen,focus=Focus(),events=trace.Count});
        };
        menu.AddHandler(Keyboard.PreviewKeyDownEvent,preview,true);menu.AddHandler(Keyboard.KeyUpEvent,up,true);
        try
        {
            if(live)
            {
                Write("ready.json",new{processId=Environment.ProcessId,startedUtcTicks=Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString(),windowHandle=new WindowInteropHelper(window).Handle.ToInt64(),window.Title,focus=Focus(),paused=settings.Paused,expanded=Field<bool>("isExpanded"),scope="Prepared own actual profile popup; taskbar visibility fixed before HWND creation, current paused state persisted and rendered. Official tool clicks its name field, presses Tab twice and Return. Observers do not handle keys or call Click. Await finish marker after observation."});
                await observed.Task.WaitAsync(TimeSpan.FromSeconds(600));
                var watch=Stopwatch.StartNew();while(!File.Exists(Path.Combine(output,"finish.txt"))){if(watch.Elapsed.TotalSeconds>180)throw new TimeoutException("Finish marker absent");await Task.Delay(75);}
            }
            else
            {
                window.Activate();wave.Focus();Keyboard.Focus(wave);check("style test starts with actual waveform keyboard focus",ReferenceEquals(Keyboard.FocusedElement,wave),Focus());
                wave.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(60);
                check("style activation saves waveform",settings.ProgressStyle=="waveform"&&store.Load().Settings.ProgressStyle=="waveform",null);
                check("style activation retains the focused style within the popup",ReferenceEquals(Keyboard.FocusedElement,wave)&&styles.Children.Contains(wave),Focus());
                check("style activation leaves the profile popup open",menu.IsOpen,null);
                // Subsequent activation must remain a usable keyboard target.
                var currentMatrix=styles.Children.OfType<Button>().First();currentMatrix.Focus();currentMatrix.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check("reverse style selection retains focus and saves matrix",ReferenceEquals(Keyboard.FocusedElement,currentMatrix)&&store.Load().Settings.ProgressStyle=="matrix",Focus());
                var color=panel.Children.OfType<System.Windows.Controls.Primitives.UniformGrid>().Single().Children.OfType<StackPanel>().Last().Children.OfType<Button>().Single();
                color.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var selected=styles.Children.OfType<Button>().Last();selected.Focus();selected.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check("style remains usable after a color switch",settings.Accent=="purple"&&settings.ProgressStyle=="waveform"&&ReferenceEquals(Keyboard.FocusedElement,selected)&&menu.IsOpen,null);
                var content=(Grid)styles.Children.OfType<Button>().Last().Content;var tick=content.Children.OfType<TextBlock>().Single();
                check("style selection mark follows the new color",tick.Text=="✓"&&tick.Foreground.ToString()=="#FFBF5AF2",tick.Foreground.ToString());
                var before=File.ReadAllBytes(store.Path);
                using(var held=new FileStream(store.Path,FileMode.Open,FileAccess.Read,FileShare.Read))
                {
                    var current=styles.Children.OfType<Button>().First();current.Focus();current.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    check("style save failure keeps current keyboard focus and popup",ReferenceEquals(Keyboard.FocusedElement,current)&&menu.IsOpen,null);
                    check("style save failure preserves disk bytes and exposes pending notice",File.ReadAllBytes(store.Path).SequenceEqual(before)&&panel.Children.OfType<TextBlock>().Last().Text.Contains("保存失败"),null);
                }
                Call("Persist");check("style save recovery persists the same selected matrix",store.Load().Settings.ProgressStyle=="matrix"&&menu.IsOpen,null);
            }
            check("keyboard style workflow preserves history and pause",settings.Paused&&engine.Current.State=="paused"&&JsonSerializer.Serialize(engine.Data.Sessions,AtomicStore.JsonOptions)==initialHistory,null);
        }
        finally
        {
            Write("style-focus-trace.json",new{live,keySeen,focus=Focus(),trace,scope="Internal WPF focus and key observers; native mode uses only official actual key input. Routed-click mode is separately identified and does not prove native keyboard behavior."});
            menu.RemoveHandler(Keyboard.PreviewKeyDownEvent,preview);menu.RemoveHandler(Keyboard.KeyUpEvent,up);menu.IsOpen=false;
            await Task.Delay(350);Call("ShowPage","home",false);Call("CollapseToTimer");Field<DispatcherTimer>("focusReturn").Stop();
        }
    }
}
