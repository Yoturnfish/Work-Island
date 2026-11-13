using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunAggregateDurationChecks(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        IEnumerable<T> FindVisual<T>(DependencyObject root) where T:DependencyObject
        {
            if(root is T match)yield return match;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in FindVisual<T>(VisualTreeHelper.GetChild(root,i)))yield return child;
        }
        var engine=Field<TrackingEngine>("engine");var store=Field<AtomicStore>("store");
        window.Left=500;window.Top=300;window.Topmost=true;
        var day=LogicalDay.Key(DateTimeOffset.Now,engine.Zone,0);var now=DateTimeOffset.Now;
        engine.Data.Settings.Paused=true;engine.Data.Settings.Rules=[];engine.Data.Settings.ReduceMotion=true;
        engine.Data.Settings.DayResetMinutes=0;engine.Data.Settings.DurationStyle="hoursMinutes";
        engine.Data.Settings.BackgroundStyle="black";engine.Data.Settings.BackgroundOpacity=1;engine.Data.Settings.IslandScale=.82;engine.ResetObservation();
        engine.Data.Sessions.Clear();
        foreach(var id in new[]{"first.exe","second.exe"})engine.Data.Sessions.Add(new(){ApplicationIdentifier=id,ApplicationName=id,DayKey=day,StartedAt=now.AddHours(-1),EndedAt=now,EffectiveSeconds=0});
        Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenHistory",null,new RoutedEventArgs());
        foreach(var id in new[]{"first.exe","second.exe"})
        {
            Call("ShowHistoryDetail",day);window.UpdateLayout();
            var hours=FindVisual<TextBox>(window).Single(b=>AutomationProperties.GetAutomationId(b)=="HistoryHours/"+day+"/"+id.ToUpperInvariant());
            var minutes=FindVisual<TextBox>(window).Single(b=>AutomationProperties.GetAutomationId(b)=="HistoryMinutes/"+day+"/"+id.ToUpperInvariant());
            hours.Text="2000000000000000";minutes.Text="0";
            hours.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window)!,0,Key.Enter){RoutedEvent=Keyboard.KeyDownEvent});
            check(id+": individually valid Mac-range history edit is retained",engine.Data.Sessions.Single(s=>s.ApplicationIdentifier==id).EffectiveSeconds==7_200_000_000_000_000_000d,null);
        }
        var disk=File.ReadAllBytes(store.Path);var memory=JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions);
        check("aggregate exceeds Int64 without invalid individual records",engine.TodaySeconds(now)==14_400_000_000_000_000_000d&&engine.TodaySeconds(now)>(double)long.MaxValue,null);
        Call("Render");
        check("compact timer displays positive aggregate hours",Field<TextBlock>("TimeLabel").Text=="4000000000000000H 00M",Field<TextBlock>("TimeLabel").Text);
        check("tray clock displays the same positive aggregate",(string)typeof(IslandWindow).GetMethod("FormatDuration",Flags|BindingFlags.Static)!.Invoke(null,new object[]{engine.TodaySeconds(now)})! =="4000000000000000:00:00",null);
        engine.Data.Settings.DurationStyle="minutesSeconds";Call("Render");
        check("minutes display does not wrap negative",Field<TextBlock>("TimeLabel").Text=="240000000000000000M 00S",Field<TextBlock>("TimeLabel").Text);
        engine.Data.Settings.DurationStyle="hoursMinutes";
        check("history compact formatter preserves positive aggregate",(string)Call("CompactTime",engine.TodaySeconds(now))! =="4000000000000000H 00M",Call("CompactTime",engine.TodaySeconds(now)));
        engine.Data.Settings.DisplayMode="menuBar";
        foreach(var pair in new[]{("statusDisplaySeconds",(object)engine.TodaySeconds(now)),("statusWorkSeconds",(object)engine.TodaySeconds(now)),("statusDurationStyle",(object)"hoursMinutes"),("statusAwayClock",(object)false)})typeof(IslandWindow).GetField(pair.Item1,Flags)!.SetValue(window,pair.Item2);
        Call("DrawMenuFrame");var status=Field<object>("statusItem");var tooltip=(string)status.GetType().GetField("tooltip",Flags)!.GetValue(status)!;
        check("native status tooltip uses the same positive aggregate",tooltip.Contains("4000000000000000H 00M")&&tooltip.Contains("4000000000000000:00:00"),tooltip);
        engine.Data.Settings.DisplayMode="floating";
        typeof(IslandWindow).GetField("historyMode",Flags)!.SetValue(window,"list");Call("RefreshHistory");var signature=Field<string>("historySignature");
        engine.Data.Sessions[0].EffectiveSeconds+=4096;Call("RefreshHistory");
        check("recent history cache distinguishes different out-of-Int64 totals",signature!=Field<string>("historySignature"),null);
        engine.Data.Sessions[0].EffectiveSeconds-=4096;
        Call("ShowHistoryStatistics");window.UpdateLayout();var strip=FindVisual<HistoryStatisticsStrip>(window).Single();
        strip.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window)!,0,Key.End){RoutedEvent=Keyboard.KeyDownEvent});
        check("statistics selection uses the same positive aggregate",Field<StackPanel>("HistoryRows").Children.OfType<TextBlock>().Single().Text==day+"  4000000000000000H 00M",Field<StackPanel>("HistoryRows").Children.OfType<TextBlock>().Single().Text);
        check("accessible statistics duration remains positive",AutomationProperties.GetName(strip)==day+" · 4000000000000000:00:00",AutomationProperties.GetName(strip));
        window.UpdateLayout();await Task.Delay(180);Call("SaveScreenRegion",Path.Combine(output,"statistics-screen.png"),0);
        // A single application can also aggregate several independently valid imported sessions.
        engine.Data.Sessions[1].ApplicationIdentifier="first.exe";Call("ShowHistoryDetail",day);window.UpdateLayout();
        var editors=FindVisual<TextBox>(window).Where(b=>AutomationProperties.GetAutomationId(b).StartsWith("HistoryHours/",StringComparison.Ordinal)).ToArray();
        check("grouped detail editor displays the represented aggregate hours",editors.Length==1&&editors[0].Text=="4000000000000000",editors.Select(e=>e.Text).ToArray());
        window.UpdateLayout();await Task.Delay(180);Call("SaveScreenRegion",Path.Combine(output,"grouped-detail-screen.png"),0);
        if(editors.Length==1)
        {
            editors[0].Text="1";editors[0].RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window)!,0,Key.Escape){RoutedEvent=Keyboard.KeyDownEvent});
            check("cancel restores positive grouped hours without changing records",editors[0].Text=="4000000000000000",editors[0].Text);
        }
        engine.Data.Sessions[1].ApplicationIdentifier="second.exe";
        check("viewing aggregate history does not change persisted values",File.ReadAllBytes(store.Path).SequenceEqual(disk)&&JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions)==memory,null);
        foreach(var seconds in new[]{0d,1.25,59.99,3600d,2_160_005_400d,TimeSpan.MaxValue.TotalSeconds,9_000_000_000_000_000_000d})
        {
            var whole=(long)seconds;var label=seconds.ToString("R",CultureInfo.InvariantCulture);
            check("ordinary clock format retained "+label,(string)typeof(IslandWindow).GetMethod("FormatDuration",Flags|BindingFlags.Static)!.Invoke(null,new object[]{seconds})! ==$"{whole/3600:00}:{whole/60%60:00}:{whole%60:00}",null);
            engine.Data.Settings.DurationStyle="hoursMinutes";
            check("ordinary hours format retained "+label,(string)Call("CompactTime",seconds)! ==$"{whole/3600}H {whole/60%60:00}M",null);
            engine.Data.Settings.DurationStyle="minutesSeconds";
            check("ordinary minutes format retained "+label,(string)Call("CompactTime",seconds)! ==$"{whole/60:00}M {whole%60:00}S",null);
        }
        engine.Data.Settings.DurationStyle="hoursMinutes";
        Call("ShowPage","home",false);Call("CollapseToTimer");Field<DispatcherTimer>("focusReturn").Stop();await Task.Delay(80);
        File.WriteAllText(Path.Combine(output,"aggregate-duration.json"),JsonSerializer.Serialize(new{day,total=engine.TodaySeconds(now),scope="Two actual WPF editor commits of individually accepted Mac-range values, aggregate display/list/statistics/detail and read-only byte preservation. Routed key events, not physical keyboard; no user data or system changes."},AtomicStore.JsonOptions));
    }
}
