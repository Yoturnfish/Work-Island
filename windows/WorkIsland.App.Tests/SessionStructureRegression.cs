using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunSessionStructureChecks(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var engine=Field<TrackingEngine>("engine");var original=JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions);var traces=new List<object>();
        var now=DateTimeOffset.Now;var day=LogicalDay.Key(now,engine.Zone,0);
        try
        {
            foreach(var schema in new[]{1,2})
            {
                var state=new AppState{SchemaVersion=schema,Settings=new(){Paused=true,Rules=[],ReduceMotion=true}};
                state.Sessions.Add(new(){ApplicationIdentifier="safe-history.exe",ApplicationName="Safe history",DayKey=day,StartedAt=now.AddHours(-1),EndedAt=now,EffectiveSeconds=3600});
                var good=JsonSerializer.SerializeToUtf8Bytes(state,AtomicStore.JsonOptions);state.Sessions[0].ApplicationIdentifier=null!;var bad=JsonSerializer.SerializeToUtf8Bytes(state,AtomicStore.JsonOptions);
                var path=Path.Combine(output,$"structural-state-{schema}.json");File.WriteAllBytes(path,bad);File.WriteAllBytes(path+".bak",good);
                var store=new AtomicStore(path);var loaded=store.Load();
                check($"schema {schema}: loader chooses verified backup before history UI",store.RecoveryMessage is not null&&loaded.Sessions.Single().ApplicationIdentifier=="safe-history.exe",new{store.RecoveryMessage,identifier=loaded.Sessions.Single().ApplicationIdentifier});
                engine.Data.Settings=loaded.Settings;engine.Data.Sessions.Clear();engine.Data.Sessions.AddRange(loaded.Sessions);engine.ResetObservation();
                string? error=null;
                try{Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenHistory",window,new RoutedEventArgs());Call("ShowHistoryDetail",day);window.UpdateLayout();}
                catch(Exception ex){error=ex.ToString();}
                check($"schema {schema}: recovered history navigation has no null-key exception",error is null,error);
                var rows=Field<StackPanel>("HistoryRows").Children.OfType<Grid>().Where(g=>g.Children.OfType<StackPanel>().Any()).ToArray();
                check($"schema {schema}: recovered application row exposes usable hour and minute editors",error is null&&rows.Length==1&&rows[0].Children.OfType<StackPanel>().Single().Children.OfType<TextBox>().Count()==2,null);
                check($"schema {schema}: recovered record identity and duration remain intact",engine.Data.Sessions.Single().Id==state.Sessions.Single().Id&&engine.Data.Sessions.Single().EffectiveSeconds==3600,null);
                check($"schema {schema}: load and view do not overwrite corrupt primary or good backup",File.ReadAllBytes(path).SequenceEqual(bad)&&File.ReadAllBytes(path+".bak").SequenceEqual(good),null);
                traces.Add(new{schema,error,store.RecoveryMessage});
                var restored=JsonSerializer.Deserialize<AppState>(original,AtomicStore.JsonOptions)!;engine.Data.Settings=restored.Settings;engine.Data.Sessions.Clear();engine.Data.Sessions.AddRange(restored.Sessions);engine.ResetObservation();
                Call("ShowPage","home",false);Call("CollapseToTimer");Field<DispatcherTimer>("focusReturn").Stop();await Task.Delay(30);
            }
        }
        finally
        {
            var restored=JsonSerializer.Deserialize<AppState>(original,AtomicStore.JsonOptions)!;engine.Data.Settings=restored.Settings;engine.Data.Sessions.Clear();engine.Data.Sessions.AddRange(restored.Sessions);engine.ResetObservation();
            File.WriteAllText(Path.Combine(output,"session-structure.json"),JsonSerializer.Serialize(new{traces,scope="Actual own primary/backup bytes and AtomicStore.Load, then controlled history navigation on actual WPF editors. Null identifier in schema1/2 primary with valid backup. Exceptions caught only by test to retain evidence; no global product exception handling or user data."},AtomicStore.JsonOptions));
        }
    }
}
