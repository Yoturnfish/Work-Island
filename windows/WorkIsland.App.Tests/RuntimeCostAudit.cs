using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    private static async Task RunRuntimeCostAudit(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        void Set(string name,object value)=>typeof(IslandWindow).GetField(name,Flags)!.SetValue(window,value);
        void Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;
        settings.HasCompletedOnboarding=true;settings.Rules=[];settings.Websites=[];settings.Paused=false;settings.GoalSeconds=14400;settings.BackgroundOpacity=1;settings.AwayTimerEnabled=false;settings.SuperReminderEnabled=false;settings.ProgressStyle="matrix";
        var now=DateTimeOffset.Now;engine.Data.Sessions.Clear();engine.Data.Sessions.Add(new(){ApplicationIdentifier="runtime-cost-owned.exe",ApplicationName="Runtime cost audit",DayKey=LogicalDay.Key(now,engine.Zone,settings.DayResetMinutes),StartedAt=now.AddMinutes(-2),EndedAt=now,EffectiveSeconds=60});
        Field<DispatcherTimer>("fullscreenTimer").Stop();window.ShowActivated=false;window.Left=700;window.Top=500;window.Topmost=true;window.Title="Work Island · 资源审计";
        Call("EnableNativeForeground");var source=(HwndSource)PresentationSource.FromVisual(window);var queries=0;var ticks=0;
        nint Hook(nint hwnd,int message,nint w,nint l,ref bool handled){if(message==0x3d)queries++;return 0;}source.AddHook(Hook);
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};timer.Tick+=(_,_)=>{ticks++;Call("Tick");};
        var rows=new List<object>();using var process=Process.GetCurrentProcess();
        void Save()=>File.WriteAllText(Path.Combine(output,"runtime-cost.json"),JsonSerializer.Serialize(new{appSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),coreSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(TrackingEngine).Assembly.Location))),processorCount=Environment.ProcessorCount,rows,scope="Independent low-progress away matrix, topmost visible nonactivating window at700,500, SoftwareOnly WPF; real native foreground/pulses and actual WindowsActivity/AtomicStore business Tick1Hz. Fullscreen polling stopped/provider controlled. Two short rounds; no screenshots, native input, own focus guard or render callbacks during timing. Includes own WM_GETOBJECT counter; no requester attribution, GPU/DWM or long-run claim. Reduced-motion and WPF phases are diagnostic controls, not product changes."},AtomicStore.JsonOptions));
        try
        {
            for(var round=0;round<2;round++)foreach(var phase in round==0?new[]{"glass-native","black-native","glass-wpf","glass-reduced","hidden"}:new[]{"hidden","glass-reduced","glass-wpf","black-native","glass-native"})
            {
                timer.Stop();Call("SuspendNativeForeground");Set("nativePulseEnabled",false);Call("SuspendNativeForeground");
                settings.BackgroundStyle=phase=="black-native"?"black":"glass";settings.ReduceMotion=phase=="glass-reduced";
                window.ShowActivated=false;window.Show();Call("NotifyPointer",false);Call("ApplyMaterial");Call("Tick");Set("awaySince",DateTimeOffset.Now.AddSeconds(-11));Call("Render");await Task.Delay(900);
                if(phase.EndsWith("-native"))Call("EnableNativePulses");
                if(phase=="hidden")window.Hide();await Task.Delay(600);
                queries=ticks=0;process.Refresh();var cpu=process.TotalProcessorTime.TotalSeconds;var bytes=TestRuntime.GetTotalAllocatedBytes(false);var handles=process.HandleCount;var captures=Field<int>("nativeForegroundCaptures");var watch=Stopwatch.StartNew();timer.Start();
                try{await Task.Delay(4100);}finally{timer.Stop();}process.Refresh();var elapsed=watch.Elapsed.TotalSeconds;
                var native=Field<bool>("nativeForegroundActive");var failed=Field<bool>("nativeForegroundFailed");var wpfIcon=Field<Grid>("StatusIcon").RenderTransform.HasAnimatedProperties;
                rows.Add(new{round,phase,elapsedSeconds=elapsed,oneCoreCpuPercent=100*(process.TotalProcessorTime.TotalSeconds-cpu)/elapsed,allocatedBytes=TestRuntime.GetTotalAllocatedBytes(false)-bytes,handleDelta=process.HandleCount-handles,queries,ticks,captures=Field<int>("nativeForegroundCaptures")-captures,window.IsVisible,window.IsActive,native,failed,wpfIcon,state=engine.Current.State});Save();
                check($"{round}/{phase} has intended live scene",!window.IsActive&&!failed&&engine.Current.State=="away"&&window.IsVisible==(phase!="hidden")&&native==phase.EndsWith("-native"),rows[^1]);
            }
        }
        finally{timer.Stop();source.RemoveHook(Hook);Call("SuspendNativeForeground");Save();}
    }
}
