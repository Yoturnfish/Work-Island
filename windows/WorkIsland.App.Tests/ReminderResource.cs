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
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    [DllImport("user32.dll",EntryPoint="GetGuiResources")]
    private static extern uint ReminderGuiResources(nint process,uint flags);
    private static async Task RunReminderResource(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        void Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;var glow=Field<FrameworkElement>("ReminderGlow");
        settings.HasCompletedOnboarding=true;settings.Paused=true;settings.Rules=[];settings.Websites=[];
        settings.ReduceMotion=false;settings.SuperReminderEnabled=false;settings.BackgroundStyle="black";
        Field<DispatcherTimer>("fullscreenTimer").Stop();window.Left=420;window.Top=310;window.Topmost=false;
        engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new(null,null,0));Call("Render");Call("CollapseToTimer");
        await Task.Delay(500);Call("EnableNativeForeground");Call("EnableNativePulses");await Task.Delay(200);
        var picker=Field<ComboBox>("ReminderPicker");
        void Select(bool value)=>picker.SelectedItem=picker.Items.Cast<ComboBoxItem>().Single(i=>i.Tag?.ToString()==(value?"true":"false"));
        using var process=Process.GetCurrentProcess();
        var rows=new List<object>();var weakClocks=new List<WeakReference>();
        async Task Pulse(bool measure,int index)
        {
            Select(false);await Task.Delay(60);process.Refresh();
            var cpu=process.TotalProcessorTime.TotalSeconds;var allocated=GC.GetTotalAllocatedBytes(false);var before=process.PrivateMemorySize64;
            var handles=process.HandleCount;var gdi=ReminderGuiResources(process.Handle,0);var user=ReminderGuiResources(process.Handle,1);
            var watch=Stopwatch.StartNew();Select(true);
            if(Field<AnimationClock?>("reminderClock") is {} clock)weakClocks.Add(new(clock));
            await Task.Delay(1680);process.Refresh();var seconds=watch.Elapsed.TotalSeconds;
            if(measure)
            {
                rows.Add(new{index,seconds,cpuSeconds=process.TotalProcessorTime.TotalSeconds-cpu,oneCorePercent=100*(process.TotalProcessorTime.TotalSeconds-cpu)/seconds,
                    allocatedBytes=GC.GetTotalAllocatedBytes(false)-allocated,privateBefore=before,privateAfter=process.PrivateMemorySize64,handlesBefore=handles,handlesAfter=process.HandleCount,
                    gdiBefore=gdi,gdiAfter=ReminderGuiResources(process.Handle,0),userBefore=user,userAfter=ReminderGuiResources(process.Handle,1),window.IsActive,
                    glow.Opacity,glow.HasAnimatedProperties,clockRemaining=Field<AnimationClock?>("reminderClock")!=null,nativeActive=Field<bool>("nativeForegroundActive")});
                check("resource pulse "+index+" ends and restores native foreground",glow.Opacity==0&&!glow.HasAnimatedProperties&&Field<AnimationClock?>("reminderClock")==null&&Field<bool>("nativeForegroundActive"),rows[^1]);
            }
        }
        for(var i=0;i<3;i++)await Pulse(false,i);
        Select(false);GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();await Task.Delay(200);
        process.Refresh();var managedBefore=GC.GetTotalMemory(false);var privateBefore=process.PrivateMemorySize64;var gdiBefore=ReminderGuiResources(process.Handle,0);var userBefore=ReminderGuiResources(process.Handle,1);
        var idleCpu=process.TotalProcessorTime.TotalSeconds;var idleWatch=Stopwatch.StartNew();await Task.Delay(3500);process.Refresh();
        var idle=new{seconds=idleWatch.Elapsed.TotalSeconds,cpuSeconds=process.TotalProcessorTime.TotalSeconds-idleCpu};
        for(var i=0;i<8;i++)await Pulse(true,i);
        Select(false);await Task.Delay(250);GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();await Task.Delay(200);process.Refresh();
        var managedAfter=GC.GetTotalMemory(false);var privateAfter=process.PrivateMemorySize64;var gdiAfter=ReminderGuiResources(process.Handle,0);var userAfter=ReminderGuiResources(process.Handle,1);var retained=weakClocks.Count(w=>w.IsAlive);
        check("completed reminder clocks become collectible",retained==0,new{created=weakClocks.Count,retained});
        check("repeated reminders retain bounded managed memory",managedAfter-managedBefore<1024*1024,new{managedBefore,managedAfter});
        check("repeated reminders leave GDI and USER handles stable",gdiAfter<=gdiBefore+1&&userAfter<=userBefore+1,new{gdiBefore,gdiAfter,userBefore,userAfter});
        window.Hide();Call("Render");check("hidden final reminder owns no animation",Field<AnimationClock?>("reminderClock")==null&&!glow.HasAnimatedProperties&&glow.Opacity==0,null);
        File.WriteAllText(Path.Combine(output,"reminder-resource.json"),JsonSerializer.Serialize(new{
            appSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),idle,rows,
            managedBefore,managedAfter,privateBefore,privateAfter,gdiBefore,gdiAfter,userBefore,userAfter,clocksCreated=weakClocks.Count,retainedClocks=retained,
            scope="Own real WPF animation and production native handoff, 3 warmup and 8 measured previews. SoftwareOnly, paused isolated data and actual selectors, no screenshots, external UI tool connection or input injection during sampling. CPU is per-process bounded sample, not GPU/DWM/system total or long-run proof; GC outside timed samples."
        },AtomicStore.JsonOptions));
    }
}
