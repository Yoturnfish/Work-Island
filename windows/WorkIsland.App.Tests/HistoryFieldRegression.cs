using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunHistoryFieldChecks(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var saved=JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions);var now=DateTimeOffset.Now;
        var today=DateOnly.ParseExact(LogicalDay.Key(now,engine.Zone,0),"yyyy-MM-dd",CultureInfo.InvariantCulture);var day=today.ToString("yyyy-MM-dd");
        var traces=new List<object>();
        try
        {
            engine.Data.Settings.Paused=true;engine.Data.Settings.Rules=[];engine.Data.Settings.ReduceMotion=true;engine.Data.Settings.DayResetMinutes=0;engine.ResetObservation();
            engine.Data.Sessions.Clear();
            foreach(var date in new[]{today,today.AddDays(-1)})foreach(var pair in new[]{("A.exe","历史"),("B.exe","Notes")})
                engine.Data.Sessions.Add(new(){ApplicationIdentifier=pair.Item1,ApplicationName=pair.Item2,DayKey=date.ToString("yyyy-MM-dd"),StartedAt=now.AddDays(date.DayNumber-today.DayNumber).AddHours(-1),EndedAt=now.AddDays(date.DayNumber-today.DayNumber),EffectiveSeconds=3600});
            var original=JsonSerializer.Serialize(engine.Data.Sessions,AtomicStore.JsonOptions);
            Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenHistory",null,new RoutedEventArgs());
            foreach(var language in new[]{"zh","en"})
            {
                engine.Data.Settings.Language=language;Call("ApplyLanguage");Call("ShowHistoryDetail",day);window.UpdateLayout();await Task.Delay(50);Field<DispatcherTimer>("focusReturn").Stop();
                var rows=Field<StackPanel>("HistoryRows").Children.OfType<Grid>().Where(g=>g.Children.OfType<StackPanel>().Any()).ToArray();
                var fields=rows.SelectMany(r=>r.Children.OfType<StackPanel>().Single().Children.OfType<TextBox>()).ToArray();
                var peers=fields.Select(f=>UIElementAutomationPeer.CreatePeerForElement(f)!).ToArray();var names=peers.Select(p=>p.GetName()).ToArray();var ids=peers.Select(p=>p.GetAutomationId()).ToArray();
                check(language+" history fields expose both units and the original application name",names.SequenceEqual(language=="zh"?new[]{"历史 · 小时","历史 · 分钟","Notes · 小时","Notes · 分钟"}:new[]{"历史 · Hours","历史 · Minutes","Notes · Hours","Notes · Minutes"}),names);
                check(language+" fields with identical values still have distinct nonempty IDs",ids.All(id=>!string.IsNullOrWhiteSpace(id))&&ids.Distinct().Count()==4,ids);
                Call("ShowHistoryDetail",day);window.UpdateLayout();
                var nextFields=Field<StackPanel>("HistoryRows").Children.OfType<Grid>().SelectMany(r=>r.Children.OfType<StackPanel>()).SelectMany(p=>p.Children.OfType<TextBox>()).ToArray();
                check(language+" reopening same detail preserves accessible field identity",nextFields.Select(f=>UIElementAutomationPeer.CreatePeerForElement(f)!.GetAutomationId()).SequenceEqual(ids),null);
                Call("ShowHistoryDetail",today.AddDays(-1).ToString("yyyy-MM-dd"));window.UpdateLayout();
                var older=Field<StackPanel>("HistoryRows").Children.OfType<Grid>().SelectMany(r=>r.Children.OfType<StackPanel>()).SelectMany(p=>p.Children.OfType<TextBox>()).Select(f=>UIElementAutomationPeer.CreatePeerForElement(f)!.GetAutomationId()).ToArray();
                check(language+" another day cannot reuse current-day field identity",older.All(id=>!ids.Contains(id))&&older.Distinct().Count()==4,older);
                traces.Add(new{language,names,ids,older});
            }
            check("reading or rebuilding field labels leaves history untouched",JsonSerializer.Serialize(engine.Data.Sessions,AtomicStore.JsonOptions)==original,null);
        }
        finally
        {
            var restored=JsonSerializer.Deserialize<AppState>(saved,AtomicStore.JsonOptions)!;engine.Data.Settings=restored.Settings;engine.Data.Sessions.Clear();engine.Data.Sessions.AddRange(restored.Sessions);engine.ResetObservation();
            typeof(IslandWindow).GetField("historyMode",Flags)!.SetValue(window,"list");Call("ApplyLanguage");Call("ShowPage","home",false);Call("CollapseToTimer");
            File.WriteAllText(Path.Combine(output,"history-fields.json"),JsonSerializer.Serialize(new{traces,scope="Actual WPF AutomationPeers for own history fields in Chinese and English; duplicate numeric values, application names, same-date reopening and other-date identity. No external UI input or user data."},AtomicStore.JsonOptions));
        }
    }
}
