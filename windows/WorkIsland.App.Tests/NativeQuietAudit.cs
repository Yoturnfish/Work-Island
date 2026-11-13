using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunNativeQuietAudit(IslandWindow window,string output)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        void Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var engine=Field<TrackingEngine>("engine");engine.Data.Settings.Rules=[new("quiet.exe","Quiet fixture")];engine.Data.Settings.BackgroundStyle="black";engine.Data.Settings.BackgroundOpacity=.5;
        engine.Data.Sessions.Add(new(){ApplicationIdentifier="quiet.exe",ApplicationName="Quiet fixture",DayKey=LogicalDay.Key(DateTimeOffset.Now,engine.Zone,engine.Data.Settings.DayResetMinutes),StartedAt=DateTimeOffset.Now.AddHours(-2),EndedAt=DateTimeOffset.Now,EffectiveSeconds=7200});
        window.Left=320;window.Top=260;engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new("quiet.exe","Quiet fixture",0));Call("ApplyMaterial");Call("Render");await Task.Delay(450);Call("EnableNativeForeground");await Task.Delay(1500);
        var counts=new Dictionary<int,int>();var layouts=0;var rows=new List<object>();
        var source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)!;
        nint Hook(nint hwnd,int message,nint w,nint l,ref bool handled){counts[message]=counts.GetValueOrDefault(message)+1;return 0;}
        void Layout(object? sender,EventArgs args)=>layouts++;
        source.AddHook(Hook);window.LayoutUpdated+=Layout;
        var other=new Window{Title="Work Island owned focus fixture",ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None,Width=64,Height=64,Left=800,Top=600};
        using var process=Process.GetCurrentProcess();
        try
        {
            other.Show();
            foreach(var phase in new[]{"initial-inactive","active","owned-other-active","hidden"})
            {
                if(phase=="active")window.Activate();else if(phase=="owned-other-active")other.Activate();else if(phase=="hidden")window.Hide();
                await Task.Delay(700);counts.Clear();layouts=0;process.Refresh();var before=process.TotalProcessorTime.TotalSeconds;var clock=Stopwatch.StartNew();await Task.Delay(4000);process.Refresh();
                rows.Add(new{phase,seconds=clock.Elapsed.TotalSeconds,oneCorePercent=100*(process.TotalProcessorTime.TotalSeconds-before)/clock.Elapsed.TotalSeconds,nativeActive=Field<bool>("nativeForegroundActive"),window.IsActive,window.IsVisible,layouts,messages=counts.OrderByDescending(p=>p.Value).Take(16).Select(p=>new{id="0x"+p.Key.ToString("X"),count=p.Value}).ToArray()});
            }
        }
        finally{window.LayoutUpdated-=Layout;source.RemoveHook(Hook);other.Close();}
        File.WriteAllText(Path.Combine(output,"native-quiet.json"),JsonSerializer.Serialize(new{rows,scope="Fresh explicitly enabled isolated controller, without preceding state/navigation/onboarding stress. Warm own-process CPU and read-only own-HWND message/layout counters. Focus actions involve only the two owned fixtures. No native stack attribution or ordinary preview acceptance."},AtomicStore.JsonOptions));
    }
}
