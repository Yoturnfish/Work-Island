using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunStateResourceControls(IslandWindow window,string output)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;
        settings.Rules=[];settings.Websites=[];settings.ReduceMotion=false;settings.BackgroundStyle="black";settings.BackgroundOpacity=.5;settings.AwayTimerEnabled=false;settings.SuperReminderEnabled=false;
        var now=DateTimeOffset.Now;engine.Data.Sessions.Clear();engine.Data.Sessions.Add(new(){ApplicationIdentifier="control-history.exe",ApplicationName="Control fixture",DayKey=LogicalDay.Key(now,engine.Zone,settings.DayResetMinutes),StartedAt=now.AddHours(-2),EndedAt=now,EffectiveSeconds=7200});
        Field<DispatcherTimer>("fullscreenTimer").Stop();window.Left=320;window.Top=260;window.Topmost=false;Call("ApplyMaterial");Call("Tick");await Task.Delay(700);Call("EnableNativeForeground");window.Hide();
        var source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)!;var mainQueries=0;var guardQueries=0;var ticks=0;
        nint MainHook(nint hwnd,int message,nint w,nint l,ref bool handled){if(message==0x3d)mainQueries++;return 0;}
        nint GuardHook(nint hwnd,int message,nint w,nint l,ref bool handled){if(message==0x3d)guardQueries++;return 0;}
        source.AddHook(MainHook);
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};timer.Tick+=(_,_)=>{ticks++;Call("Tick");};
        var guard=new Window{Title="Work Island owned control fixture",WindowStyle=WindowStyle.None,Width=64,Height=64,Left=800,Top=600,ShowActivated=false,ShowInTaskbar=false};
        HwndSource? guardSource=null;var guardClosed=false;var rows=new List<object>();using var process=Process.GetCurrentProcess();
        void Save()=>File.WriteAllText(Path.Combine(output,"state-resource-controls.json"),JsonSerializer.Serialize(new{productDllSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),processorCount=Environment.ProcessorCount,rows,scope="Fresh isolated actual window. Own-process CPU with separate main/guard WM_GETOBJECT counters. Only owned windows activated/closed; no external activation or input. No cache, frame-rate, accessibility, product rendering or global process setting changes. Real WindowsActivity/AtomicStore Tick when enabled. Not GPU/DWM or long-run acceptance."},AtomicStore.JsonOptions));
        async Task Measure(string phase,int round,double seconds)
        {
            await Task.Delay(800);mainQueries=guardQueries=ticks=0;process.Refresh();var cpu=process.TotalProcessorTime.TotalSeconds;var allocation=TestRuntime.GetTotalAllocatedBytes(false);var watch=Stopwatch.StartNew();
            await Task.Delay(TimeSpan.FromSeconds(seconds));process.Refresh();var elapsed=watch.Elapsed.TotalSeconds;
            rows.Add(new{phase,round,activity=engine.Current.State,settings.ReduceMotion,elapsedSeconds=elapsed,oneCorePercent=100*(process.TotalProcessorTime.TotalSeconds-cpu)/elapsed,allocatedBytes=TestRuntime.GetTotalAllocatedBytes(false)-allocation,mainQueries,guardQueries,ticks,businessEnabled=timer.IsEnabled,window.IsVisible,window.IsActive,guardVisible=!guardClosed&&guard.IsVisible,guardActive=!guardClosed&&guard.IsActive,nativeActive=Field<bool>("nativeForegroundActive")});Save();
        }
        try
        {
            await Measure("hidden-idle",0,6);timer.Start();await Measure("hidden-business",0,6);
            guard.Show();guardSource=HwndSource.FromHwnd(new WindowInteropHelper(guard).Handle)!;guardSource.AddHook(GuardHook);guard.Activate();
            await Measure("hidden-business-guard",0,6);timer.Stop();await Measure("hidden-guard",0,6);
            guardSource.RemoveHook(GuardHook);guard.Close();guardClosed=true;
            await Measure("hidden-idle-after-guard",0,6);timer.Start();
            for(var round=0;round<2;round++)foreach(var activity in new[]{"away","paused"})
            {
                settings.Paused=activity=="paused";settings.ReduceMotion=false;window.ShowActivated=false;window.Show();Call("Tick");
                if(activity=="away"){typeof(IslandWindow).GetField("awaySince",Flags)!.SetValue(window,(DateTimeOffset?)DateTimeOffset.Now.AddSeconds(-11));Call("Render");}
                await Measure("visible-motion",round,6);
                settings.ReduceMotion=true;Call("Render");await Measure("visible-reduced",round,6);
            }
            window.Hide();await Measure("final-hidden-business",0,6);
            File.WriteAllText(Path.Combine(output,"final-hidden-clocks.json"),JsonSerializer.Serialize(AuditControllerClocks(window),AtomicStore.JsonOptions));
        }
        finally{timer.Stop();source.RemoveHook(MainHook);if(!guardClosed){guardSource?.RemoveHook(GuardHook);guard.Close();}}
        Save();
    }
}
