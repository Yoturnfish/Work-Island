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
    private static async Task RunNativePulseControllerResource(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;var progress=Field<PixelProgress>("GoalProgress");
        settings.HasCompletedOnboarding=true;settings.Rules=[];settings.Websites=[];settings.GoalSeconds=14400;settings.ReduceMotion=false;settings.BackgroundStyle="black";settings.BackgroundOpacity=.5;settings.AwayTimerEnabled=false;settings.SuperReminderEnabled=false;
        var now=DateTimeOffset.Now;engine.Data.Sessions.Clear();engine.Data.Sessions.Add(new(){ApplicationIdentifier="resource-fixture.exe",ApplicationName="Resource fixture",DayKey=LogicalDay.Key(now,engine.Zone,settings.DayResetMinutes),StartedAt=now.AddHours(-2),EndedAt=now,EffectiveSeconds=7200});
        Field<DispatcherTimer>("fullscreenTimer").Stop();window.Left=320;window.Top=260;window.Topmost=false;Call("ApplyMaterial");Call("NotifyPointer",false);Call("Tick");await Task.Delay(500);Call("EnableNativeForeground");
        var source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)!;var queries=0;var ticks=0;
        nint Hook(nint hwnd,int message,nint w,nint l,ref bool handled){if(message==0x3d)queries++;return 0;}source.AddHook(Hook);
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};timer.Tick+=(_,_)=>{ticks++;Call("Tick");};var rows=new List<object>();var audits=new List<object>();using var process=Process.GetCurrentProcess();
        int Count(string field,string property){var item=typeof(IslandWindow).GetField(field,Flags)!.GetValue(window);return item==null?0:(int)item.GetType().GetProperty(property,Flags)!.GetValue(item)!;}
        void Save()=>File.WriteAllText(Path.Combine(output,"pulse-resource.json"),JsonSerializer.Serialize(new{productDllSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),wpfRenderMode=source.CompositionTarget.RenderMode.ToString(),rows,audits,scope="Real current production controller explicitly opted in versus its own WPF fallback; WPF render mode recorded separately. Actual WindowsActivity/AtomicStore Tick 1Hz, isolated half-progress data, no screenshots or focus guard during timing; captures, WM_GETOBJECT and active native controllers recorded. Native timing excludes cold construction, DWM/GPU, external processes and long-run/hardware acceptance."},AtomicStore.JsonOptions));
        var cases=new List<(int Round,string State,string Style,string Renderer,bool AwayClock)>();
        for(var round=0;round<2;round++)foreach(var state in new[]{"away","paused"})foreach(var style in new[]{"matrix","waveform"})foreach(var renderer in new[]{"native","wpf"})cases.Add((round,state,style,renderer,false));
        cases.Add((2,"away","matrix","native",true));cases.Add((2,"away","matrix","wpf",true));
        try
        {
            foreach(var (round,state,style,renderer,awayClock) in cases)
            {
                Call("SuspendNativeForeground");typeof(IslandWindow).GetField("nativePulseEnabled",Flags)!.SetValue(window,false);Call("SuspendNativeForeground");settings.Paused=state=="paused";settings.ProgressStyle=style;settings.AwayTimerEnabled=awayClock;Call("Tick");
                if(state=="away"){typeof(IslandWindow).GetField("awaySince",Flags)!.SetValue(window,(DateTimeOffset?)DateTimeOffset.Now.AddSeconds(-11));Call("Render");}
                await Task.Delay(500);if(renderer=="native")Call("EnableNativePulses");await Task.Delay(600);
                queries=ticks=0;var captures=Field<int>("nativeForegroundCaptures");var glyphUploads=Count("nativeGlyphSurface","Uploads");var cellsUploads=Count("nativeCellsSurface","Uploads");process.Refresh();var cpu=process.TotalProcessorTime.TotalSeconds;var allocation=TestRuntime.GetTotalAllocatedBytes(false);var memory=process.PrivateMemorySize64;var handles=process.HandleCount;var watch=Stopwatch.StartNew();timer.Start();
                try{await Task.Delay(4100);}finally{timer.Stop();}process.Refresh();var seconds=watch.Elapsed.TotalSeconds;var cpuSeconds=process.TotalProcessorTime.TotalSeconds-cpu;
                var active=Field<bool>("nativeForegroundActive");var wpfStatus=Field<System.Windows.Controls.Grid>("StatusIcon").RenderTransform.HasAnimatedProperties;var wpfCells=((System.Windows.Shapes.Path)typeof(PixelProgress).GetField("unfilled",Flags)!.GetValue(progress)!).HasAnimatedProperties;var glyph=Count("nativeGlyphSurface","ActivePulses");var cells=Count("nativeCellsSurface","ActivePulses");
                rows.Add(new{round,state,style,renderer,awayClock,actualState=engine.Current.State,seconds,oneCorePercent=100*cpuSeconds/seconds,allocatedBytes=TestRuntime.GetTotalAllocatedBytes(false)-allocation,privateBytesBefore=memory,privateBytesAfter=process.PrivateMemorySize64,handlesBefore=handles,handlesAfter=process.HandleCount,mainQueries=queries,ticks,captures=Field<int>("nativeForegroundCaptures")-captures,glyphUploads=Count("nativeGlyphSurface","Uploads")-glyphUploads,cellsUploads=Count("nativeCellsSurface","Uploads")-cellsUploads,window.IsActive,active,wpfStatus,wpfCells,glyphControllers=glyph,cellsControllers=cells,parentDevices=Count("nativeForegroundSurface","DeviceCreates"),glyphDevices=Count("nativeGlyphSurface","DeviceCreates"),cellsDevices=Count("nativeCellsSurface","DeviceCreates")});
                check($"{round}/{state}/{style}/{renderer} measures real ticking renderer",engine.Current.State==state&&ticks==4&&!window.IsActive&&(renderer=="native"?active&&!wpfStatus&&!wpfCells&&glyph==2&&cells==(state=="away"?1:0):!active&&wpfStatus&&wpfCells==(state=="away")),rows[^1]);
                if(awayClock&&renderer=="native")check("1Hz actual away-clock tick updates foreground but reuses pulse textures",Field<int>("nativeForegroundCaptures")-captures==ticks&&Count("nativeGlyphSurface","Uploads")==glyphUploads&&Count("nativeCellsSurface","Uploads")==cellsUploads,rows[^1]);
                if(renderer=="native")audits.Add(new{round,state,style,audit=AuditControllerClocks(window)});Save();
            }
        }
        finally{timer.Stop();source.RemoveHook(Hook);Call("SuspendNativeForeground");Save();}
    }
}
