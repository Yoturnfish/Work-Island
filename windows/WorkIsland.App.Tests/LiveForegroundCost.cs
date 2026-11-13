using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunLiveForegroundCost(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;settings.HasCompletedOnboarding=true;settings.BackgroundStyle="glass";settings.BackgroundOpacity=1;settings.Rules=[new("cost.exe","Cost")];settings.ReduceMotion=false;settings.SuperReminderEnabled=false;
        Field<DispatcherTimer>("fullscreenTimer").Stop();Call("ApplyMaterial");engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new("cost.exe","Cost",0));Call("Render");await Task.Delay(500);Call("EnableLiveForeground");await Task.Delay(5000);
        using var process=Process.GetCurrentProcess();var rows=new List<object>();
        foreach(var state in new[]{"tracking","away","paused"})
        {
            settings.Paused=state=="paused";engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new(state=="tracking"?"cost.exe":"other.exe","Cost",0));Call("Render");
            if(state=="away"){typeof(IslandWindow).GetField("awaySince",Flags)!.SetValue(window,(DateTimeOffset?)DateTimeOffset.Now.AddSeconds(-11));Call("Render");}
            await Task.Delay(700);process.Refresh();var before=process.TotalProcessorTime.TotalSeconds;var at=Stopwatch.GetTimestamp();await Task.Delay(6000);process.Refresh();var seconds=Stopwatch.GetElapsedTime(at).TotalSeconds;var single=100*(process.TotalProcessorTime.TotalSeconds-before)/seconds;
            rows.Add(new{state,seconds,singleCoreCpuPercent=single,machineEquivalentCpuPercent=single/Environment.ProcessorCount,captures=Field<int>("nativeForegroundCaptures"),renderMode=(System.Windows.PresentationSource.FromVisual(window) as System.Windows.Interop.HwndSource)?.CompositionTarget.RenderMode.ToString()});
            check("live "+state+" keeps bounded process CPU without full-window captures",single<15&&Field<int>("nativeForegroundCaptures")==0,rows[^1]);
        }
        File.WriteAllText(Path.Combine(output,"runtime.json"),JsonSerializer.Serialize(new{logicalProcessors=Environment.ProcessorCount,rows,scope="Isolated live production foreground, native progress scan, controlled activity, no UIA session or screen sampler during measured intervals. Process CPU only, not GPU/DWM or a long-term guarantee."},AtomicStore.JsonOptions));
    }
}
