using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunPauseResetChecks(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var pausedData=new AppState{Settings=new(){Paused=true,Rules=[]}};var paused=new TrackingEngine(pausedData,TimeZoneInfo.Utc);
        check("paused settings initialize the engine in paused state",paused.Current.State=="paused"&&paused.Current.Target is null,null);
        paused.ResetObservation();check("reset preserves manual pause without an observation",paused.Current.State=="paused"&&paused.LastObservation is null,null);
        var at=new DateTimeOffset(2026,11,9,12,0,0,TimeSpan.Zero);paused.Observe(at,new(null,null,0),1);paused.ResetObservation();
        check("reset of observed pause has no target or elapsed baseline",paused.Current.State=="paused"&&paused.Current.Target is null&&paused.LastObservation is null&&pausedData.Sessions.Count==0,null);
        var work=new AppState{Settings=new(){Rules=[new("pause-reset.exe","Reset fixture")]}};var tracking=new TrackingEngine(work,TimeZoneInfo.Utc);
        tracking.Observe(at,new("pause-reset.exe","Reset fixture",0),10);tracking.Observe(at.AddSeconds(2),new("pause-reset.exe","Reset fixture",0),12);work.Settings.Paused=true;tracking.ResetObservation();
        check("pause reset retains completed work and removes active target",tracking.Current.State=="paused"&&tracking.Current.Target is null&&tracking.TodaySeconds(at.AddSeconds(2))==2,null);
        for(var i=0;i<100;i++)tracking.ResetObservation();
        check("repeated reset cannot turn pause into away or add work",tracking.Current.State=="paused"&&tracking.LastObservation is null&&tracking.TodaySeconds(at.AddSeconds(2))==2,null);
        work.Settings.Paused=false;tracking.ResetObservation();
        check("unpaused reset remains conservative away without invented tracking",tracking.Current.State=="away"&&tracking.Current.Target is null,null);
        tracking.Observe(at.AddHours(1),new("pause-reset.exe","Reset fixture",0),3612);
        check("first resumed observation cannot credit reset gap",tracking.Current.State=="tracking"&&tracking.TodaySeconds(at.AddHours(1))==2,null);
        tracking.Observe(at.AddHours(1).AddSeconds(1),new("pause-reset.exe","Reset fixture",0),3613);
        check("fresh observed interval after resume still earns exact work",tracking.TodaySeconds(at.AddHours(1))==3,null);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;settings.Paused=true;settings.Rules=[];settings.ReduceMotion=false;settings.BackgroundStyle="black";
        engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new(null,null,0));Call("Render");await Task.Delay(450);
        var original=settings.ActiveProfileId!;var observations=new List<object>();
        object Snapshot(string action)=>new{action,settings.Paused,state=engine.Current.State,visual=Field<string>("visualState"),stateFade=Field<ClockGroup?>("stateTransition")!=null,coffee=Field<FrameworkElement>("CoffeeIcon").Opacity,pause=Field<FrameworkElement>("PauseIcon").Opacity};
        async Task Action(string name,Action act)
        {
            engine.Observe(DateTimeOffset.Now,new(null,null,0));Call("Render");await Task.Delay(400);act();var snapshot=Snapshot(name);observations.Add(snapshot);
            check(name+" keeps logical manual pause immediately",settings.Paused&&engine.Current.State=="paused",snapshot);
            check(name+" cannot start a false away crossfade",Field<string>("visualState")=="paused"&&Field<ClockGroup?>("stateTransition")==null,snapshot);
        }
        await Action("create profile",()=>Call("CreateProfile"));var created=settings.ActiveProfileId!;
        await Action("activate default profile",()=>Call("ActivateProfile",original));
        await Action("switch color slot",()=>{Call("SwitchColorProfile","blue");Call("ProfileChanged");});
        await Action("delete active profile",()=>Call("DeleteCurrentProfile"));
        await Action("refresh suspension",()=>{typeof(IslandWindow).GetField("sessionLocked",Flags)!.SetValue(window,true);Call("RefreshSuspension");});
        typeof(IslandWindow).GetField("sessionLocked",Flags)!.SetValue(window,false);Call("RefreshSuspension");
        engine.Observe(DateTimeOffset.Now,new(null,null,0));Call("Render");await Task.Delay(400);Call("TimeChanged",null,EventArgs.Empty);await Task.Delay(50);Call("Render");
        check("clock correction also preserves logical pause",engine.Current.State=="paused"&&settings.Paused,Snapshot("clock"));
        check("clock correction cannot trigger coffee transition while paused",Field<string>("visualState")=="paused"&&Field<ClockGroup?>("stateTransition")==null,Snapshot("clock"));
        engine.Observe(DateTimeOffset.Now,new(null,null,0));Call("Render");await Task.Delay(450);
        var curve=new List<object>();var watch=System.Diagnostics.Stopwatch.StartNew();var allPaused=true;Call("CreateProfile");
        while(watch.Elapsed.TotalSeconds<1.15)
        {
            var state=engine.Current.State;var visual=Field<string>("visualState");var coffee=Field<FrameworkElement>("CoffeeIcon").Opacity;var pause=Field<FrameworkElement>("PauseIcon").Opacity;var stateFade=Field<ClockGroup?>("stateTransition")!=null;
            allPaused&=state=="paused"&&visual=="paused"&&coffee==0&&pause==1&&!stateFade;curve.Add(new{seconds=watch.Elapsed.TotalSeconds,state,visual,coffee,pause,stateFade});await Task.Delay(16);
        }
        check("profile reset leaves paused icon fully presented throughout sampled interval",allPaused&&curve.Count>10,new{samples=curve.Count});
        File.WriteAllText(Path.Combine(output,"pause-reset.json"),JsonSerializer.Serialize(new{appSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),coreSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(TrackingEngine).Assembly.Location))),observations,curve,scope="Actual Core state and WPF presentation under controlled own profile, suspension and clock handlers. Paused is user state, observation reset discards only interval/target. Timed samples are WPF properties, not screen/compositor frames or real OS session actions."},AtomicStore.JsonOptions));
    }
}
