using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunNativeWarmAudit(IslandWindow window,string output,Action<string,bool,object?> check,bool blackFirst)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        void Set(string name,object value)=>typeof(IslandWindow).GetField(name,Flags)!.SetValue(window,value);
        void Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;
        var pulseElement=(FrameworkElement)typeof(PixelProgress).GetProperty("PulseElement",Flags)!.GetValue(Field<PixelProgress>("GoalProgress"))!;
        settings.HasCompletedOnboarding=true;settings.Rules=[];settings.Websites=[];settings.Paused=false;settings.GoalSeconds=14400;settings.BackgroundOpacity=1;settings.AwayTimerEnabled=false;settings.SuperReminderEnabled=false;settings.ProgressStyle="matrix";settings.ReduceMotion=false;
        var now=DateTimeOffset.Now;engine.Data.Sessions.Clear();engine.Data.Sessions.Add(new(){ApplicationIdentifier="warm-audit-owned.exe",ApplicationName="Warm audit",DayKey=LogicalDay.Key(now,engine.Zone,0),StartedAt=now.AddMinutes(-2),EndedAt=now,EffectiveSeconds=60});
        Field<DispatcherTimer>("fullscreenTimer").Stop();window.ShowActivated=false;window.Left=700;window.Top=500;window.Topmost=true;window.Title="Work Island · 原生暖机审计";
        Call("EnableNativeForeground");var source=(HwndSource)PresentationSource.FromVisual(window);var queries=0;var ticks=0;
        nint Hook(nint hwnd,int message,nint w,nint l,ref bool handled){if(message==0x3d)queries++;return 0;}source.AddHook(Hook);
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};timer.Tick+=(_,_)=>{ticks++;Call("Tick");};var rows=new List<object>();using var process=Process.GetCurrentProcess();
        int Count(string field,string property){var value=typeof(IslandWindow).GetField(field,Flags)!.GetValue(window);return value is null?0:(int)value.GetType().GetProperty(property,Flags)!.GetValue(value)!;}
        Dictionary<int,double> Threads(){var result=new Dictionary<int,double>();foreach(ProcessThread thread in process.Threads)try{result[thread.Id]=thread.TotalProcessorTime.TotalSeconds;}catch(System.ComponentModel.Win32Exception){}return result;}
        var phases=blackFirst?new[]{("black",4),("glass",6),("black",4)}:new[]{("glass",6),("black",4),("glass",4)};
        void Save()=>File.WriteAllText(Path.Combine(output,"native-warm.json"),JsonSerializer.Serialize(new{appSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),coreSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(TrackingEngine).Assembly.Location))),blackFirst,processorCount=Environment.ProcessorCount,rows,scope="Fresh isolated low-progress away matrix, topmost nonactivating at700,500, SoftwareOnly WPF, real production native foreground and pulses; original business Tick1Hz. Fullscreen polling stopped/provider controlled. Consecutive5.1s CPU blocks, read-only own WPF clock audits between blocks; no screenshot/input/focus guard or frame callbacks. Whole-process/thread CPU excludes DWM/GPU; WM_GETOBJECT0 does not exclude cached UIA calls. No user data or product rendering changes."},AtomicStore.JsonOptions));
        try
        {
            var phaseIndex=0;
            foreach(var (material,blocks) in phases)
            {
                timer.Stop();Call("SuspendNativeForeground");Set("nativePulseEnabled",false);Call("SuspendNativeForeground");settings.BackgroundStyle=material;Call("NotifyPointer",false);Call("ApplyMaterial");Call("Tick");Set("awaySince",DateTimeOffset.Now.AddSeconds(-11));Call("Render");await Task.Delay(900);Call("EnableNativePulses");await Task.Delay(600);
                for(var block=0;block<blocks;block++)
                {
                    queries=ticks=0;process.Refresh();var threads=Threads();var cpu=process.TotalProcessorTime.TotalSeconds;var captures=Field<int>("nativeForegroundCaptures");var watch=Stopwatch.StartNew();timer.Start();
                    try{await Task.Delay(5100);}finally{timer.Stop();}process.Refresh();var elapsed=watch.Elapsed.TotalSeconds;var cpuSeconds=process.TotalProcessorTime.TotalSeconds-cpu;var endThreads=Threads();
                    var audit=JsonSerializer.SerializeToElement(AuditControllerClocks(window),AtomicStore.JsonOptions);var activeClocks=audit.GetProperty("clocks").EnumerateArray().Count(c=>c.GetProperty("state").GetString()=="Active");
                    var native=Field<bool>("nativeForegroundActive");var failed=Field<bool>("nativeForegroundFailed");var wpfIcon=Field<Grid>("StatusIcon").RenderTransform.HasAnimatedProperties;var wpfCells=pulseElement.HasAnimatedProperties;
                    rows.Add(new{phaseIndex,material,block,elapsedSeconds=elapsed,oneCoreCpuPercent=100*cpuSeconds/elapsed,queries,ticks,captures=Field<int>("nativeForegroundCaptures")-captures,native,failed,wpfIcon,wpfCells,statusPulseNative=Field<bool>("statusPulseNative"),glyphPulses=Count("nativeGlyphSurface","ActivePulses"),cellsPulses=Count("nativeCellsSurface","ActivePulses"),window.IsVisible,window.IsActive,activeWpfClocks=activeClocks,clockAuditFailure=audit.GetProperty("failure"),clocks=audit,threads=endThreads.Where(p=>threads.ContainsKey(p.Key)).Select(p=>new{id=p.Key,cpuSeconds=p.Value-threads[p.Key]}).Where(p=>p.cpuSeconds>0).OrderByDescending(p=>p.cpuSeconds).ToArray()});Save();
                    check($"{phaseIndex}/{material}/{block} keeps intended native background scene",native&&!failed&&!window.IsActive&&window.IsVisible&&engine.Current.State=="away",null);
                }
                phaseIndex++;
            }
        }
        finally{timer.Stop();source.RemoveHook(Hook);Call("SuspendNativeForeground");Save();}
    }
}
