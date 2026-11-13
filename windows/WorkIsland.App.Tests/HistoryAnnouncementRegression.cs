using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunHistoryAnnouncementChecks(IslandWindow window,string output,Action<string,bool,object?> check,bool live)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var store=Field<AtomicStore>("store");var saved=JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions);
        var today=DateOnly.ParseExact(LogicalDay.Key(DateTimeOffset.Now,engine.Zone,0),"yyyy-MM-dd",CultureInfo.InvariantCulture);var day=today.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        var observations=new List<object>();var nativeErrors=new List<string>();var now=DateTimeOffset.Now;
        try
        {
            engine.Data.Settings.Paused=true;engine.Data.Settings.Rules=[];engine.Data.Settings.DayResetMinutes=0;engine.Data.Settings.ReduceMotion=true;engine.Data.Settings.BackgroundStyle="black";
            engine.Data.Settings.DurationStyle="hoursMinutes";engine.ResetObservation();engine.Data.Sessions.Clear();
            engine.Data.Sessions.Add(new(){ApplicationIdentifier="large-history.exe",ApplicationName="Large history",DayKey=day,StartedAt=now.AddHours(-1),EndedAt=now,EffectiveSeconds=3600});
            window.Title=live?"Work Island · 历史大数实测":"Work Island · 历史大数检查";window.ShowInTaskbar=live;window.Left=340;window.Top=260;
            Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenHistory",null,new RoutedEventArgs());Call("ShowHistoryDetail",day);await Task.Delay(100);Field<DispatcherTimer>("focusReturn").Stop();
            var row=Field<StackPanel>("HistoryRows").Children.OfType<Grid>().Single(g=>g.Children.OfType<StackPanel>().Any());var editor=row.Children.OfType<StackPanel>().Single();
            var hours=(TextBox)editor.Children[0];var minutes=(TextBox)editor.Children[2];hours.Text="1000000000";minutes.Text="59";
            hours.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window)!,0,Key.Enter){RoutedEvent=Keyboard.KeyDownEvent});
            const double target=3_600_000_003_540;
            check("Mac accepted large history input is retained exactly",engine.TodaySeconds(now)==target&&store.Load().Sessions.Sum(s=>s.EffectiveSeconds)==target,null);
            var disk=File.ReadAllBytes(store.Path);var memory=JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions);
            Call("ShowHistoryStatistics");window.UpdateLayout();await Task.Delay(150);
            var strip=(HistoryStatisticsStrip)Field<StackPanel>("HistoryRows").Children.OfType<ScrollViewer>().Single().Content;
            void End(HistoryStatisticsStrip element)=>element.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window)!,0,Key.End){RoutedEvent=Keyboard.KeyDownEvent});
            if(live)
            {
                DispatcherUnhandledExceptionEventHandler onError=(_,args)=>
                {
                    if(args.Exception is not OverflowException)return;
                    nativeErrors.Add(args.Exception.ToString());args.Handled=true;
                };
                Application.Current.DispatcherUnhandledException+=onError;
                try
                {
                    window.Activate();strip.Focus();Keyboard.Focus(strip);
                    var keys=0;KeyEventHandler onKey=(_,args)=>{if(args.Key==Key.End)keys++;};window.PreviewKeyUp+=onKey;
                    try
                    {
                        File.WriteAllText(Path.Combine(output,"input-ready.json"),JsonSerializer.Serialize(new{processId=RuntimeCompat.ProcessId,window.Title,day,target,scope="Independent real WPF history editor accepted the same large value as the Mac test; store contains it. Statistics focused during preparation, await only official actual End key. Test-only dispatcher observer records OverflowException and keeps own fixture alive; it is not a product exception handler."},AtomicStore.JsonOptions));
                        var marker=Path.Combine(output,"native-complete.txt");var watch=Stopwatch.StartNew();
                        while(!File.Exists(marker)&&watch.Elapsed.TotalSeconds<180)await Task.Delay(100);
                        check("official End key reached prepared statistics window",keys>0&&File.Exists(marker),new{keys});
                        check("real statistics key does not raise dispatcher overflow",nativeErrors.Count==0,nativeErrors.ToArray());
                        check("real keyboard selection updates date and exact large total",Field<StackPanel>("HistoryRows").Children.OfType<TextBlock>().Single().Text==day+"  1000000000H 59M",Field<StackPanel>("HistoryRows").Children.OfType<TextBlock>().Single().Text);
                        check("native view preserves exact original data",JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions)==memory&&File.ReadAllBytes(store.Path).SequenceEqual(disk),null);
                        observations.Add(new{phase="native",name=AutomationProperties.GetName(strip),errors=nativeErrors.ToArray()});
                    }
                    finally{window.PreviewKeyUp-=onKey;}
                }
                finally{Application.Current.DispatcherUnhandledException-=onError;}
                return;
            }
            string? failure=null;try{End(strip);}catch(Exception error){failure=error.ToString();}
            check("accepted large history remains selectable without overflow",failure is null,failure);
            check("selected large history date exposes its exact visible total",Field<StackPanel>("HistoryRows").Children.OfType<TextBlock>().Single().Text==day+"  1000000000H 59M",Field<StackPanel>("HistoryRows").Children.OfType<TextBlock>().Single().Text);
            check("large accessible duration has complete positive hours and minutes",AutomationProperties.GetName(strip)==day+" · 1000000000:59:00",AutomationProperties.GetName(strip));
            foreach(var seconds in new[]{0d,1.25,86401.125,TestRuntime.BitDecrement(TimeSpan.MaxValue.TotalSeconds),TimeSpan.MaxValue.TotalSeconds,target,9_000_000_000_000_000_000d})
            {
                var sample=new HistoryStatisticsStrip(new Dictionary<DateOnly,double>{{today,seconds}},today,14400,System.Windows.Media.Colors.Green,window.FontFamily);
                double? announced=null;sample.HoverChanged+=(date,value)=>{if(date==today)announced=value;};sample.Measure(new Size(210,56));sample.Arrange(new Rect(0,0,210,56));
                failure=null;try{End(sample);}catch(Exception error){failure=error.ToString();}
                check("statistics supports accepted seconds "+seconds.ToString("R",CultureInfo.InvariantCulture),failure is null&&announced==seconds,new{seconds,announced,failure});
                if(seconds<TimeSpan.MaxValue.TotalSeconds)
                    check("ordinary constant duration format retained "+seconds.ToString("R",CultureInfo.InvariantCulture),AutomationProperties.GetName(sample)==day+" · "+TimeSpan.FromSeconds(seconds).ToString("c",CultureInfo.InvariantCulture),AutomationProperties.GetName(sample));
                observations.Add(new{seconds,name=AutomationProperties.GetName(sample),failure});
            }
            check("viewing and announcing history never changes saved records",JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions)==memory&&File.ReadAllBytes(store.Path).SequenceEqual(disk),null);
            strip.SetHorizontalOffset(0);await Task.Delay(50);
        }
        finally
        {
            var restored=JsonSerializer.Deserialize<AppState>(saved,AtomicStore.JsonOptions)!;engine.Data.Settings=restored.Settings;engine.Data.Sessions.Clear();engine.Data.Sessions.AddRange(restored.Sessions);engine.ResetObservation();
            typeof(IslandWindow).GetField("historyMode",Flags)!.SetValue(window,"list");Call("ShowPage","home",false);Call("CollapseToTimer");Field<DispatcherTimer>("focusReturn").Stop();
            File.WriteAllText(Path.Combine(output,"history-announcement.json"),JsonSerializer.Serialize(new{live,observations,scope="Own isolated WPF history editor / AtomicStore, Mac large accepted value, keyboard selection and duration boundaries. Routine raises own routed events; live awaits official actual keyboard and uses an explicit test-only OverflowException observer. No real clock/day/OS change, ordinary user data or long-run acceptance."},AtomicStore.JsonOptions));
        }
    }
}
