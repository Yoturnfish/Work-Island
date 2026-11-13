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
    private static async Task RunStateResourceAudit(IslandWindow window,string output)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;
        settings.Rules=[];settings.Websites=[];settings.ReduceMotion=false;settings.BackgroundStyle="black";settings.BackgroundOpacity=.5;settings.AwayTimerEnabled=false;settings.SuperReminderEnabled=false;
        var now=DateTimeOffset.Now;engine.Data.Sessions.Clear();engine.Data.Sessions.Add(new(){ApplicationIdentifier="resource-history.exe",ApplicationName="Resource fixture",DayKey=LogicalDay.Key(now,engine.Zone,settings.DayResetMinutes),StartedAt=now.AddHours(-2),EndedAt=now,EffectiveSeconds=7200});
        Field<DispatcherTimer>("fullscreenTimer").Stop();window.Left=320;window.Top=260;window.Topmost=false;
        var progress=Field<PixelProgress>("GoalProgress");var icon=Field<FrameworkElement>("StatusIcon");var dot=Field<FrameworkElement>("StatusDot");
        var filled=(FrameworkElement)typeof(PixelProgress).GetField("filled",Flags)!.GetValue(progress)!;
        var unfilled=(FrameworkElement)typeof(PixelProgress).GetField("unfilled",Flags)!.GetValue(progress)!;
        Call("ApplyMaterial");Call("Tick");await Task.Delay(800);Call("EnableNativeForeground");await Task.Delay(300);
        var source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)!;var objectQueries=0;var paints=0;var layouts=0;
        nint Hook(nint hwnd,int message,nint w,nint l,ref bool handled){if(message==0x3d)objectQueries++;else if(message==0xf)paints++;return 0;}
        void Layout(object? sender,EventArgs args)=>layouts++;
        source.AddHook(Hook);window.LayoutUpdated+=Layout;
        var guard=new Window{Title="Work Island owned resource focus fixture",WindowStyle=WindowStyle.None,Width=64,Height=64,Left=800,Top=600,ShowActivated=false,ShowInTaskbar=false};
        var business=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};var businessTicks=0;
        business.Tick+=(_,_)=>{businessTicks++;Call("Tick");};
        var rows=new List<object>();using var process=Process.GetCurrentProcess();
        void Save()=>File.WriteAllText(Path.Combine(output,"state-resource.json"),JsonSerializer.Serialize(new{productDllSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),processorCount=Environment.ProcessorCount,renderTier=RenderCapability.Tier>>16,dpi=VisualTreeHelper.GetDpi(window).PixelsPerDip,rows,scope="Isolated actual IslandWindow, native controller enabled, real WindowsActivity and AtomicStore Tick at 1Hz; no matching application rules so no work credited. Own-process CPU includes owned focus guard. Cache variants are diagnostic only, not production changes. No screenshots or per-frame observers during measurements; WM_GETOBJECT and layout only counted. Not GPU/DWM, normal-user-preview or all-day acceptance."},AtomicStore.JsonOptions));
        async Task Measure(string phase,int round,string activity,double seconds)
        {
            await Task.Delay(800);objectQueries=paints=layouts=businessTicks=0;process.Refresh();
            var before=process.TotalProcessorTime.TotalSeconds;var allocated=GC.GetTotalAllocatedBytes(false);var watch=Stopwatch.StartNew();
            await Task.Delay(TimeSpan.FromSeconds(seconds));process.Refresh();var elapsed=watch.Elapsed.TotalSeconds;
            rows.Add(new{phase,round,activity,elapsedSeconds=elapsed,oneCorePercent=100*(process.TotalProcessorTime.TotalSeconds-before)/elapsed,allocatedBytes=GC.GetTotalAllocatedBytes(false)-allocated,businessTicks,objectQueries,paints,layouts,window.IsVisible,window.IsActive,guardActive=guard.IsActive,nativeActive=Field<bool>("nativeForegroundActive"),nativeFailed=Field<bool>("nativeForegroundFailed"),progress.PulseAnimating});Save();
        }
        try
        {
            guard.Show();guard.Activate();business.Start();
            for(var round=0;round<2;round++)
            {
                window.Hide();await Measure("hidden",round,"away",3);
                foreach(var activity in new[]{"away","paused"})
                {
                    settings.Paused=activity=="paused";window.ShowActivated=false;window.Show();Call("Tick");
                    if(activity=="away"){typeof(IslandWindow).GetField("awaySince",Flags)!.SetValue(window,(DateTimeOffset?)DateTimeOffset.Now.AddSeconds(-11));Call("Render");}
                    guard.Activate();
                    foreach(var variant in new[]{"normal","icon-cache","layers-cache","all-cache"})
                    {
                        icon.CacheMode=variant is "icon-cache" or "all-cache"?new BitmapCache{RenderAtScale=2}:null;
                        foreach(var layer in new[]{dot,filled,unfilled})layer.CacheMode=variant is "layers-cache" or "all-cache"?new BitmapCache{RenderAtScale=2}:null;
                        await Measure(variant,round,activity,6);
                    }
                }
            }
            icon.CacheMode=dot.CacheMode=filled.CacheMode=unfilled.CacheMode=null;
            window.Activate();await Measure("active-normal",0,"paused",3);
            guard.Activate();await Measure("inactive-normal",0,"paused",3);
        }
        finally
        {
            business.Stop();window.LayoutUpdated-=Layout;source.RemoveHook(Hook);guard.Close();
            icon.CacheMode=dot.CacheMode=filled.CacheMode=unfilled.CacheMode=null;
        }
        Save();
    }
}
