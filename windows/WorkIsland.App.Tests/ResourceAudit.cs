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
using System.Windows.Media;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunCompactResourceAudit(IslandWindow window,string output,bool observeTransforms)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var saved=JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions);
        var progress=Field<PixelProgress>("GoalProgress");var scan=(TranslateTransform)typeof(PixelProgress).GetField("scan",Flags)!.GetValue(progress)!;
        var scale=(ScaleTransform)Field<FrameworkElement>("StatusIcon").RenderTransform;
        var samples=new List<object>();var scanChanges=0;var pulseChanges=0;
        EventHandler scanChanged=(_,_)=>scanChanges++;EventHandler pulseChanged=(_,_)=>pulseChanges++;
        if(observeTransforms){scan.Changed+=scanChanged;scale.Changed+=pulseChanged;}
        using var own=Process.GetCurrentProcess();
        try
        {
            engine.Data.Settings.DisplayMode="floating";engine.Data.Settings.Rules=[new("resource-audit.exe","Resource audit")];engine.Data.Settings.Websites=[];
            for(var round=0;round<3;round++)foreach(var phase in new[]{"compact-motion","expanded-motion","expanded-static"})
            {
                var expanded=Field<bool>("isExpanded");
                if(phase=="compact-motion"&&expanded)Call("CollapseToTimer");
                else if(phase!="compact-motion"&&!expanded)Call("ToggleExpanded",null!,new RoutedEventArgs());
                if(phase!="compact-motion")Call("ShowPage","settings",false);
                engine.Data.Settings.ReduceMotion=phase=="expanded-static";
                engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new("resource-audit.exe","Resource audit",0));Call("Render");
                await Task.Delay(700);window.UpdateLayout();
                scanChanges=0;pulseChanges=0;own.Refresh();var cpu=own.TotalProcessorTime;var watch=Stopwatch.StartNew();
                await Task.Delay(3000);own.Refresh();watch.Stop();var cpuSeconds=(own.TotalProcessorTime-cpu).TotalSeconds;
                samples.Add(new{round,phase,elapsedSeconds=watch.Elapsed.TotalSeconds,cpuSeconds,oneCorePercent=100*cpuSeconds/watch.Elapsed.TotalSeconds,machinePercent=100*cpuSeconds/watch.Elapsed.TotalSeconds/Environment.ProcessorCount,scanChanges,pulseChanges,scanAnimating=progress.ScanAnimating,pulseAnimating=progress.PulseAnimating,compactVisible=Field<FrameworkElement>("CompactPanel").IsVisible});
            }
            File.WriteAllText(Path.Combine(output,"compact-resource.json"),JsonSerializer.Serialize(new{auditCompleted=true,observeTransforms,productDllSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),processorCount=Environment.ProcessorCount,samples,method="Same own WPF process; tracking state and timer stopped; three 3-second samples per phase after 700ms warmup. Process CPU; optional animated-transform Changed callbacks. No external input.",limitation="Short bounded samples are not all-day acceptance. Instrumented and uninstrumented runs are separate; machine load and compositor can vary."},AtomicStore.JsonOptions));
        }
        finally
        {
            if(observeTransforms){scan.Changed-=scanChanged;scale.Changed-=pulseChanged;}var restored=JsonSerializer.Deserialize<AppState>(saved,AtomicStore.JsonOptions)!;engine.Data.Settings=restored.Settings;engine.Data.Sessions.Clear();engine.Data.Sessions.AddRange(restored.Sessions);engine.ResetObservation();Call("CollapseToTimer");Call("ProfileChanged");
        }
    }
    private static int RunBrowserLiveProbe(string output)
    {
        Directory.CreateDirectory(output);var source=new WindowsRecognitionSource();
        var applications=source.Applications().Where(app=>RecognitionRules.Browsers.Contains(app.Identifier,StringComparer.OrdinalIgnoreCase)).ToArray();var results=new List<object>();
        foreach(var application in applications)
        {
            var watch=Stopwatch.StartNew();var worker=Task.Run(()=>source.CurrentPage(application));
            var completed=Task.WhenAny(worker,Task.Delay(3000)).GetAwaiter().GetResult()==worker;
            if(!completed){results.Add(new{browser=application.Identifier,window=(long)application.Window,timedOut=true});break;}
            var page=worker.GetAwaiter().GetResult();
            results.Add(new{browser=application.Identifier,window=(long)application.Window,timedOut=false,elapsedMilliseconds=watch.Elapsed.TotalMilliseconds,addressAvailable=page.Address is not null,unavailableReason=page.Unavailable,validHttpAddress=Uri.TryCreate(page.Address,UriKind.Absolute,out var uri)&&uri.Scheme is "http" or "https"});
        }
        File.WriteAllText(Path.Combine(output,"browser-live.json"),JsonSerializer.Serialize(new{observedAt=DateTimeOffset.Now,productDllSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),browserWindows=applications.Length,results,scope="Read-only call to the production UIA address adapter on currently enumerated browser windows. No URL, domain, page title or document content retained; no activation, navigation or browser changes.",acceptance="Availability observation only; not an add/remove, input-source or multi-browser acceptance."},AtomicStore.JsonOptions));
        return 0;
    }
}
