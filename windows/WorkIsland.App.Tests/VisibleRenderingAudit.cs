using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    [DllImport("kernel32.dll",EntryPoint="GetCurrentThreadId")]private static extern uint AuditGetCurrentThreadId();
    private static async Task RunVisibleThreadAudit(IslandWindow window,string output)
    {
        var engine=(TrackingEngine)typeof(IslandWindow).GetField("engine",Flags)!.GetValue(window)!;
        var settings=engine.Data.Settings;settings.Rules=[new("thread-audit.exe","Thread audit")];settings.Websites=[];
        using var own=Process.GetCurrentProcess();var uiThread=AuditGetCurrentThreadId();var samples=new List<object>();
        Dictionary<int,(double Cpu,string Module)> Threads()
        {
            var result=new Dictionary<int,(double,string)>();own.Refresh();
            foreach(ProcessThread thread in own.Threads)
            {
                using(thread)try
                {
                    var address=thread.StartAddress.ToInt64();var module=own.Modules.Cast<ProcessModule>().FirstOrDefault(m=>address>=m.BaseAddress.ToInt64()&&address<m.BaseAddress.ToInt64()+m.ModuleMemorySize)?.ModuleName??"unknown";
                    result[thread.Id]=(thread.TotalProcessorTime.TotalSeconds,module);
                }
                catch(Exception error)when(error is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException){}
            }
            return result;
        }
        await Task.Delay(1500);
        for(var round=0;round<2;round++)foreach(var phase in new[]{"static","scan"})
        {
            settings.ReduceMotion=phase=="static";engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new("thread-audit.exe","Thread audit",0));typeof(IslandWindow).GetMethod("Render",Flags|BindingFlags.DeclaredOnly)!.Invoke(window,null);
            await Task.Delay(1000);var before=Threads();var watch=Stopwatch.StartNew();await Task.Delay(4000);var after=Threads();watch.Stop();
            var usage=after.Select(pair=>new{threadId=pair.Key,isUiThread=pair.Key==uiThread,module=pair.Value.Module,cpuSeconds=Math.Max(0,pair.Value.Cpu-(before.TryGetValue(pair.Key,out var old)?old.Cpu:0))}).Where(item=>item.cpuSeconds>0).OrderByDescending(item=>item.cpuSeconds).ToArray();
            samples.Add(new{round,phase,seconds=watch.Elapsed.TotalSeconds,threads=usage});
        }
        File.WriteAllText(Path.Combine(output,"visible-threads.json"),JsonSerializer.Serialize(new{auditCompleted=true,uiThread,renderTier=RenderCapability.Tier>>16,productDllSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),samples,scope="Read own-process thread CPU counters only, no per-frame observers. Thread start modules are best-effort labels, not stack attribution."},AtomicStore.JsonOptions));
    }
    private static async Task RunVisibleRenderingAudit(IslandWindow window,string output)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var saved=JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions);
        var progress=Field<PixelProgress>("GoalProgress");
        T Pixel<T>(string name)=>(T)typeof(PixelProgress).GetField(name,Flags)!.GetValue(progress)!;
        var filled=Pixel<System.Windows.Shapes.Path>("filled");var unfilled=Pixel<System.Windows.Shapes.Path>("unfilled");
        var filledScan=Pixel<Canvas>("filledScan");var unfilledScan=Pixel<Canvas>("unfilledScan");
        var samples=new List<object>();using var own=Process.GetCurrentProcess();
        try
        {
            engine.Data.Settings.Rules=[new("render-audit.exe","Render audit")];engine.Data.Settings.Websites=[];engine.Data.Settings.ReduceMotion=false;engine.Data.Sessions.Clear();
            var now=DateTimeOffset.Now;engine.Data.Sessions.Add(new(){ApplicationIdentifier="render-audit.exe",ApplicationName="Render audit",DayKey=LogicalDay.Key(now,engine.Zone,engine.Data.Settings.DayResetMinutes),StartedAt=now.AddHours(-1),EndedAt=now,EffectiveSeconds=7200});
            engine.ResetObservation();engine.Observe(now,new("render-audit.exe","Render audit",0));Call("Render");await Task.Delay(2000);window.UpdateLayout();
            var glow=filled.Effect;var originalFilledCache=filled.CacheMode;var originalUnfilledCache=unfilled.CacheMode;
            for(var round=0;round<3;round++)foreach(var phase in new[]{"static","normal-scan","cached-static-layers","no-glow","scan-masked"})
            {
                engine.Data.Settings.ReduceMotion=phase=="static";Call("Render");
                filled.CacheMode=phase=="cached-static-layers"?new BitmapCache():originalFilledCache;
                unfilled.CacheMode=phase=="cached-static-layers"?new BitmapCache():originalUnfilledCache;
                filled.Effect=phase=="no-glow"?null:glow;
                filledScan.Visibility=unfilledScan.Visibility=phase is "static" or "scan-masked"?Visibility.Collapsed:Visibility.Visible;
                window.UpdateLayout();await Task.Delay(700);
                own.Refresh();var cpu=own.TotalProcessorTime;var watch=Stopwatch.StartNew();await Task.Delay(2500);own.Refresh();watch.Stop();var cpuSeconds=(own.TotalProcessorTime-cpu).TotalSeconds;
                samples.Add(new{round,phase,elapsedSeconds=watch.Elapsed.TotalSeconds,cpuSeconds,oneCorePercent=cpuSeconds/watch.Elapsed.TotalSeconds*100,scanAnimating=progress.ScanAnimating,renderTier=RenderCapability.Tier>>16});
            }
            filled.CacheMode=originalFilledCache;unfilled.CacheMode=originalUnfilledCache;filled.Effect=glow;
            File.WriteAllText(Path.Combine(output,"visible-rendering.json"),JsonSerializer.Serialize(new{auditCompleted=true,productDllSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),processorCount=Environment.ProcessorCount,renderTier=RenderCapability.Tier>>16,isRemoteSession=SystemParameters.IsRemoteSession,processRenderMode=System.Windows.Media.RenderOptions.ProcessRenderMode.ToString(),windowRenderMode=((HwndSource)PresentationSource.FromVisual(window)).CompositionTarget.RenderMode.ToString(),dpi=VisualTreeHelper.GetDpi(window).PixelsPerDip,window.ActualWidth,window.ActualHeight,progressWidth=progress.ActualWidth,progressHeight=progress.ActualHeight,samples,method="Three 2.5-second own-process CPU samples per diagnostic phase, 700ms settling, 2s initial warmup; business timer stopped, no per-frame observers or external input.",limitation="Cache, glow removal and masking are diagnostic changes in this isolated fixture only. No product rendering or frame-rate setting changed; short samples are not all-day acceptance."},AtomicStore.JsonOptions));
        }
        finally
        {
            filled.CacheMode=unfilled.CacheMode=null;filledScan.Visibility=unfilledScan.Visibility=Visibility.Visible;
            var restored=JsonSerializer.Deserialize<AppState>(saved,AtomicStore.JsonOptions)!;engine.Data.Settings=restored.Settings;engine.Data.Sessions.Clear();engine.Data.Sessions.AddRange(restored.Sessions);engine.ResetObservation();Call("ProfileChanged");
        }
    }
}

