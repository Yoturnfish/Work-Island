using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunDayResetChecks(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var store=Field<AtomicStore>("store");var reset=Field<MacDayResetInput>("ResetInput");
        var saved=JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions);var records=new List<object>();
        var zone=TimeZoneInfo.Local;var date=TimeZoneInfo.ConvertTime(DateTimeOffset.Now,zone).Date;
        DateTimeOffset At(DateTime local)=>new(local,zone.GetUtcOffset(local));
        Session Record(DateTimeOffset start,double seconds,double wallSeconds)=>new(){ApplicationIdentifier="day-reset-fixture.exe",ApplicationName="Daily reset fixture",CategoryRawValue="editing",StartedAt=start,EndedAt=start.AddSeconds(wallSeconds),EffectiveSeconds=seconds,DayKey=LogicalDay.Key(start,zone,0)};
        string Metadata()=>JsonSerializer.Serialize(engine.Data.Sessions.Select(s=>new{s.Id,s.ApplicationIdentifier,s.ApplicationName,s.CategoryRawValue,s.StartedAt,s.EndedAt,s.EffectiveSeconds}),AtomicStore.JsonOptions);
        string[] Keys()=>engine.Data.Sessions.Select(s=>s.DayKey).ToArray();
        void Change(int minutes)
        {
            reset.HourField.Text=(minutes==0?24:minutes/60).ToString("00",CultureInfo.InvariantCulture);
            reset.MinuteField.Text=(minutes%60).ToString("00",CultureInfo.InvariantCulture);reset.FinishEditing();
        }
        async Task Settings()
        {
            if(!Field<bool>("isExpanded"))Call("ToggleExpanded",null,new RoutedEventArgs());
            Call("ShowPage","settings",false);reset.BringIntoView();window.UpdateLayout();await Task.Delay(100);
        }
        try
        {
            engine.Data.Settings.Paused=true;engine.Data.Settings.ReduceMotion=true;engine.Data.Settings.DayResetMinutes=0;engine.Data.Settings.Rules=[];engine.Data.Settings.BackgroundStyle="black";
            engine.Data.Settings.DurationStyle="hoursMinutes";engine.Zone=zone;engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new(null,null,0));reset.Minutes=0;
            engine.Data.Sessions.Clear();engine.Data.Sessions.AddRange([Record(At(date.AddMinutes(30)),1800,1800),Record(At(date.AddHours(4)),3600,3600),Record(At(date.AddDays(-1).AddHours(12)),7200,3600)]);
            var originalKeys=Keys();var metadata=Metadata();var total=engine.Data.Sessions.Sum(s=>s.EffectiveSeconds);
            Call("Persist");Call("Render");window.Left=320;window.Top=260;window.Topmost=true;await Task.Delay(150);
            Call("SaveScreenRegion",Path.Combine(output,"before-reset-screen.png"),0);
            await Settings();Change(180);
            var expected=engine.Data.Sessions.Select(s=>LogicalDay.Key(s.StartedAt,zone,180)).ToArray();
            check("daily reset control changes the saved preference",engine.Data.Settings.DayResetMinutes==180&&reset.Minutes==180&&store.Load().Settings.DayResetMinutes==180,null);
            check("changing reset while paused preserves paused decision",engine.Current.State=="paused",new{engine.Current.State});
            check("existing records move to logical dates defined by their start",Keys().SequenceEqual(expected),new{actual=Keys(),expected});
            check("today uses newly grouped history instead of old date keys",engine.TodaySeconds(At(date.AddHours(6)))==3600,new{actual=engine.TodaySeconds(At(date.AddHours(6)))});
            check("regrouping preserves IDs metadata timestamps manual totals and record count",Metadata()==metadata&&engine.Data.Sessions.Sum(s=>s.EffectiveSeconds)==total,null);
            var persisted=store.Load();check("disk contains both new reset and regrouped dates",persisted.Sessions.Select(s=>s.DayKey).SequenceEqual(expected),null);
            records.Add(new{phase="changed-03",minutes=engine.Data.Settings.DayResetMinutes,keys=Keys(),total});
            File.Copy(store.Path,Path.Combine(output,"changed-state.json"));
            Call("CollapseToTimer");await Task.Delay(150);Call("Render");await Task.Delay(100);Call("SaveScreenRegion",Path.Combine(output,"after-reset-screen.png"),0);
            await Settings();Change(0);
            check("return to midnight restores original grouping",Keys().SequenceEqual(originalKeys)&&engine.Data.Settings.DayResetMinutes==0,null);
            check("repeated reset changes cannot duplicate or change total work",Metadata()==metadata&&engine.Data.Sessions.Sum(s=>s.EffectiveSeconds)==total,null);
            foreach(var pair in new[]{(from:0,to:180),(from:180,to:0)})
            {
                Change(pair.from);Call("Persist");var beforeBytes=File.ReadAllBytes(store.Path);var beforeKeys=Keys();
                window.Activate();reset.BringIntoView();window.UpdateLayout();reset.HourField.Focus();Keyboard.Focus(reset.HourField);await Task.Delay(40);
                check($"{pair} save failure fixture edits the actual focused control",reset.IsKeyboardFocusWithin,null);
                using(var locked=new FileStream(store.Path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                {
                    Change(pair.to);
                    check($"{pair} failed save restores reset preference and backing control",engine.Data.Settings.DayResetMinutes==pair.from&&reset.Minutes==pair.from,new{engine.Data.Settings.DayResetMinutes,reset.Minutes});
                    check($"{pair} failed save restores visible focused fields",reset.HourField.Text==(pair.from==0?"24":"03")&&reset.MinuteField.Text=="00",new{reset.HourField.Text,minute=reset.MinuteField.Text});
                    check($"{pair} failed save restores every original date key",Keys().SequenceEqual(beforeKeys),null);
                    check($"{pair} failed save keeps metadata and total work",Metadata()==metadata&&engine.Data.Sessions.Sum(s=>s.EffectiveSeconds)==total,null);
                }
                check($"{pair} failed save never changes main file bytes",File.ReadAllBytes(store.Path).SequenceEqual(beforeBytes),null);
                Call("Tick");
                check($"{pair} background saver cannot later commit rejected setting",store.Load().Settings.DayResetMinutes==pair.from&&store.Load().Sessions.Select(s=>s.DayKey).SequenceEqual(beforeKeys),null);
                Change(pair.to);
                check($"{pair} retry after unlocking commits grouping and preference together",store.Load().Settings.DayResetMinutes==pair.to&&Keys().SequenceEqual(engine.Data.Sessions.Select(s=>LogicalDay.Key(s.StartedAt,zone,pair.to))),null);
                records.Add(new{phase="failure-retry",pair,minutes=engine.Data.Settings.DayResetMinutes,keys=Keys()});
            }
            Keyboard.ClearFocus();Change(0);engine.Data.Settings.Paused=false;engine.Data.Settings.AwayThreshold=3600;engine.Data.Settings.Rules=[new("failed-checkpoint-fixture.exe","Failed checkpoint")];engine.ResetObservation();Call("Persist");
            engine.Observe(DateTimeOffset.Now.AddSeconds(-1),new("failed-checkpoint-fixture.exe","Failed checkpoint",0),Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency-1);
            double failedCheckpoint;
            using(var locked=new FileStream(store.Path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            {
                Change(180);failedCheckpoint=engine.Data.Sessions.Where(s=>s.ApplicationIdentifier=="failed-checkpoint-fixture.exe").Sum(s=>s.EffectiveSeconds);
                check("failed active reset retains checkpointed work under old preference",failedCheckpoint>.7&&failedCheckpoint<3&&engine.Data.Settings.DayResetMinutes==0,new{failedCheckpoint,engine.Data.Settings.DayResetMinutes});
            }
            Call("Tick");var recoveredCheckpoint=store.Load();
            check("background recovery saves pending work without rejected reset",recoveredCheckpoint.Settings.DayResetMinutes==0&&recoveredCheckpoint.Sessions.Where(s=>s.ApplicationIdentifier=="failed-checkpoint-fixture.exe").Sum(s=>s.EffectiveSeconds)>=failedCheckpoint,null);
            File.Copy(store.Path,Path.Combine(output,"checkpoint-recovered-state.json"));records.Add(new{phase="active-failure-recovery",failedCheckpoint});
            // Retire only this owned synthetic sample before the independent
            // successful-checkpoint case, so their seeded intervals do not overlap.
            engine.Data.Sessions.RemoveAll(s=>s.ApplicationIdentifier=="failed-checkpoint-fixture.exe");
            Change(0);engine.Data.Settings.Rules=[new("checkpoint-fixture.exe","Checkpoint")];engine.ResetObservation();
            engine.Observe(DateTimeOffset.Now.AddSeconds(-1),new("checkpoint-fixture.exe","Checkpoint",0),Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency-1);
            Change(180);var checkpoint=engine.Data.Sessions.Where(s=>s.ApplicationIdentifier=="checkpoint-fixture.exe").Sum(s=>s.EffectiveSeconds);
            check("reset change checkpoints pending work once before regrouping",checkpoint>.7&&checkpoint<3,new{checkpoint});
            check("regrouped observation does not credit the same checkpoint twice",engine.LastObservation is null,null);
            var boundary=At(date.AddHours(3));var before=engine.Data.Sessions.Sum(s=>s.EffectiveSeconds);
            engine.Observe(boundary.AddSeconds(-1),new("checkpoint-fixture.exe","Checkpoint",0));engine.Observe(boundary.AddSeconds(1),new("checkpoint-fixture.exe","Checkpoint",0));
            check("new observations use new daily boundary and conserve elapsed time",Math.Abs(engine.Data.Sessions.Sum(s=>s.EffectiveSeconds)-before-2)<.00001,null);
            check("new interval splits between the previous and current logical dates",engine.Data.Sessions.TakeLast(2).Select(s=>s.DayKey).SequenceEqual(new[]{date.AddDays(-1).ToString("yyyy-MM-dd"),date.ToString("yyyy-MM-dd")}),null);
            engine.Data.Settings.Paused=true;engine.ResetObservation();Change(0);
            engine.Data.Sessions.Add(new(){ApplicationIdentifier="earliest.exe",ApplicationName="Earliest fixture",StartedAt=DateTimeOffset.MinValue,EndedAt=DateTimeOffset.MinValue,DayKey="0001-01-01",EffectiveSeconds=0});
            Call("Persist");var extremeBytes=File.ReadAllBytes(store.Path);var extremeKeys=Keys();
            Change(1439);
            check("unrepresentable regrouping keeps preference and every date unchanged",engine.Data.Settings.DayResetMinutes==0&&reset.Minutes==0&&Keys().SequenceEqual(extremeKeys),null);
            check("unrepresentable regrouping preserves durable bytes",File.ReadAllBytes(store.Path).SequenceEqual(extremeBytes),null);
        }
        finally
        {
            engine.ResetObservation();var restored=JsonSerializer.Deserialize<AppState>(saved,AtomicStore.JsonOptions)!;engine.Data.Settings=restored.Settings;engine.Data.Sessions.Clear();engine.Data.Sessions.AddRange(restored.Sessions);
            Keyboard.ClearFocus();reset.Minutes=restored.Settings.DayResetMinutes;Call("Render");Call("CollapseToTimer");
            File.WriteAllText(Path.Combine(output,"day-reset.json"),JsonSerializer.Serialize(new{productDllSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),zone=zone.Id,records,scope="Actual owned WPF reset control, isolated records with manual effective time, real main-file locks and backing storage, checkpoint/public engine boundary checks. No OS clock/reset/timezone change or user data modification. Rekey existing records by start time as Mac; no historical interval splitting."},AtomicStore.JsonOptions));
        }
    }
}
