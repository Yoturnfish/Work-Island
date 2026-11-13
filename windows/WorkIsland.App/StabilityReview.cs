using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using WorkIsland.Core;

namespace WorkIsland.App;

public partial class IslandWindow
{
    public bool StabilityReview { get; set; }
    public bool BackgroundStabilityReview { get; set; }
    public int StabilitySeconds { get; set; } = 600;
    [DllImport("user32.dll")] private static extern uint GetGuiResources(nint process,uint flags);
    private sealed record ResourceSample(double Seconds,long PrivateBytes,long WorkingSetBytes,long ManagedBytes,int Handles,uint GdiObjects,uint UserObjects,double CpuSeconds);

    private async Task RunStabilityReview()
    {
        timer.Stop();focusReturn.Stop();
        var output=smokeOutput ?? Path.Combine(Path.GetDirectoryName(store.Path)!,"stability-review");
        Directory.CreateDirectory(output);
        var samples=new List<ResourceSample>();var intervals=new List<double>();var checks=new List<object>();
        var watch=Stopwatch.StartNew();var operations=0;var observations=0;
        var backgroundVisibilityViolation=false;
        var observedLogicalDays=new HashSet<string>();
        var before=0d;var nextOperation=0d;var nextSample=0d;
        void Check(string name,bool passed,object? evidence=null)
        {checks.Add(new{name,passed,evidence});if(!passed)SmokeFailed=true;}
        using var process=Process.GetCurrentProcess();
        ResourceSample Sample()
        {
            process.Refresh();return new(watch.Elapsed.TotalSeconds,process.PrivateMemorySize64,process.WorkingSet64,
                GC.GetTotalMemory(false),process.HandleCount,GetGuiResources(process.Handle,0),GetGuiResources(process.Handle,1),process.TotalProcessorTime.TotalSeconds);
        }
        void WriteProgress(string status)
        {
            var path=Path.Combine(output,"stability-progress.json");
            File.WriteAllText(path+".tmp",JsonSerializer.Serialize(new{status,elapsedSeconds=watch.Elapsed.TotalSeconds,requestedSeconds=StabilitySeconds,operations,observations,background=BackgroundStabilityReview,backgroundVisibilityViolation,observedLogicalDays,sampleCount=samples.Count,samples=samples.TakeLast(6).ToArray()},AtomicStore.JsonOptions));
            RuntimeCompat.MoveFile(path+".tmp",path,true);
        }
        try
        {
            // Isolated historical fixture. No matching rules: the real process
            // sampler must never turn synthetic history into current work.
            var settings=engine.Data.Settings;settings.Rules=[];settings.Paused=false;settings.Topmost=false;
            settings.DisplayTitle="稳定性验收";settings.IslandScale=.82;settings.BackgroundStyle="black";
            settings.ColorProfiles=[];settings.ActiveProfileId=null;
            ColorProfilesService.Initialize(settings);
            settings.ReduceMotion=false;settings.DayResetMinutes=180;
            Topmost=false;Left=200;Top=200;
            if(BackgroundStabilityReview)Hide();
            var today=DateOnly.ParseExact(LogicalDay.Key(DateTimeOffset.Now,engine.Zone,180),"yyyy-MM-dd");
            engine.Data.Sessions.Clear();
            for(var n=0;n<1500;n++)
            {
                var date=today.AddDays(-1-n/20);var instant=new DateTimeOffset(date.ToDateTime(new TimeOnly(12,0)),TimeZoneInfo.Local.GetUtcOffset(date.ToDateTime(new TimeOnly(12,0))));
                engine.Data.Sessions.Add(new(){ApplicationIdentifier="fixture.exe",ApplicationName="Historical fixture",DayKey=date.ToString("yyyy-MM-dd"),StartedAt=instant,EndedAt=instant.AddSeconds(60),EffectiveSeconds=60});
            }
            engine.ResetObservation();visualState="";Tick();
            var historicalSeconds=engine.Data.Sessions.Sum(s=>s.EffectiveSeconds);
            while(watch.Elapsed.TotalSeconds<StabilitySeconds)
            {
                var elapsed=watch.Elapsed.TotalSeconds;
                if(before>0)intervals.Add(elapsed-before);before=elapsed;
                Tick();observations++;
                observedLogicalDays.Add(LogicalDay.Key(DateTimeOffset.Now,engine.Zone,settings.DayResetMinutes));
                if(BackgroundStabilityReview && (IsVisible || IsActive))backgroundVisibilityViolation=true;
                if(elapsed>=nextOperation)
                {
                    switch(operations%10)
                    {
                        case 0:if(!isExpanded)ToggleExpanded(null,new RoutedEventArgs());break;
                        case 1:ShowPage("settings",false);break;
                        case 2:ShowPage("history",false);break;
                        case 3:ShowPage("recognition",false);break;
                        case 4:CollapseToTimer();break;
                        case 5:settings.BackgroundStyle="glass";ApplyMaterial();break;
                        case 6:settings.BackgroundStyle="black";ApplyMaterial();break;
                        case 7:Hide();break;
                        case 8:if(!BackgroundStabilityReview)Show();ClampToScreen();break;
                        case 9:TogglePause(null,new RoutedEventArgs());break;
                    }
                    operations++;nextOperation=elapsed+10;Persist();
                }
                if(elapsed>=nextSample){samples.Add(Sample());WriteProgress("running");nextSample=elapsed+10;}
                await Task.Delay(1000);
            }
            settings.Paused=false;settings.BackgroundStyle="black";if(!BackgroundStabilityReview)Show();CollapseToTimer();ApplyMaterial();Tick();
            await Task.Delay(300);samples.Add(Sample());
            var baseline=samples.First(s=>s.Seconds>=30);var final=samples[^1];
            Check("real isolated process remains responsive for requested duration",watch.Elapsed.TotalSeconds>=StabilitySeconds,new{requested=StabilitySeconds,actual=watch.Elapsed.TotalSeconds,observations,operations});
            Check("historical fixture unchanged and unmatched foreground earns no work",engine.Data.Sessions.Sum(s=>s.EffectiveSeconds)==historicalSeconds && engine.TodaySeconds(DateTimeOffset.Now)==0);
            var persisted=new AtomicStore(store.Path).Load();
            Check("atomic state reload matches in-memory settings and history",JsonSerializer.Serialize(persisted,AtomicStore.JsonOptions)==JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions));
            Check("no dispatcher exception log was generated",!File.Exists(Path.Combine(Path.GetDirectoryName(store.Path)!,"errors.log")));
            if(BackgroundStabilityReview)
                Check("background review stayed hidden without activating its window",!backgroundVisibilityViolation && !IsVisible && !IsActive);
            else
                Check("final page transition has one settled visible layer",!isExpanded && CompactPanel.IsVisible && !DetailsPanel.IsVisible);
            Check("native window resources remain bounded",final.Handles<=baseline.Handles+100 && final.GdiObjects<=baseline.GdiObjects+20 && final.UserObjects<=baseline.UserObjects+40,new{baseline,final});
            Check("private memory growth after warmup remains under 96 MiB",final.PrivateBytes-baseline.PrivateBytes<96*1024*1024,new{growthBytes=final.PrivateBytes-baseline.PrivateBytes});
            var sorted=intervals.Order().ToArray();
            File.WriteAllText(Path.Combine(output,"stability-review.json"),JsonSerializer.Serialize(new
            {
                passed=!SmokeFailed,version=typeof(IslandWindow).Assembly.GetName().Version!.ToString(),
                requestedSeconds=StabilitySeconds,elapsedSeconds=watch.Elapsed.TotalSeconds,observations,operations,
                background=BackgroundStabilityReview,backgroundVisibilityViolation,observedLogicalDays,
                historicalRecords=1500,historicalSeconds,checks,samples,
                callbackIntervals=new{count=sorted.Length,mean=sorted.Average(),maximum=sorted[^1],p99=sorted[(int)Math.Floor((sorted.Length-1)*.99)]},
                dpi=System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip,
                monitorCount=System.Windows.Forms.Screen.AllScreens.Length,
                limitations=new[]{"Real elapsed-time app/resource/persistence soak with isolated synthetic history and no matching rules; does not claim real work-duration accumulation.","Page/material transitions are app-scripted; background mode never shows the window and does not verify visible rendering or manual input.","No physical lock, sleep, display topology or DPI change. Only the actual completed elapsed duration is established; logical-day observations do not by themselves prove active-work allocation across the boundary."}
            },AtomicStore.JsonOptions));
            WriteProgress(SmokeFailed?"failed":"complete");
        }
        catch(Exception error)
        {SmokeFailed=true;File.WriteAllText(Path.Combine(output,"stability-error.txt"),error.ToString());}
        finally{exiting=true;Close();}
    }
}
