using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunReleaseHandoff(IslandWindow window,string output,Action<string,bool,object?> check,bool live=false)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;
        settings.HasCompletedOnboarding=true;settings.ReduceMotion=false;settings.BackgroundStyle="glass";settings.BackgroundOpacity=1;settings.SuperReminderEnabled=false;settings.Rules=[new("handoff.exe","Fixture")];
        Field<DispatcherTimer>("fullscreenTimer").Stop();window.Left=340;window.Top=280;Call("ApplyMaterial");Call("NotifyPointer",false);
        engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new("handoff.exe","Fixture",0));Call("Render");await Task.Delay(500);
        Call("EnableNativeForeground");Call("EnableNativePulses");await Task.Delay(180);
        bool Active()=>Field<bool>("nativeForegroundActive");int Captures()=>Field<int>("nativeForegroundCaptures");
        check("production native foreground is active before the interaction sequence",Active(),null);
        var before=Captures();var surface=Field<object>("nativeForegroundSurface");
        Call("NativeForegroundDeactivated",null,EventArgs.Empty);
        check("losing focus never withdraws the current foreground texture",Active(),null);
        await Task.Delay(80);check("unchanged focus loss does not upload another identical texture",Active()&&Captures()==before,new{before,after=Captures()});
        var samples=new List<object>();
        async Task Navigation(bool expand)
        {
            Call(expand?"ToggleExpanded":"CollapseToTimer",expand?new object?[]{null,new RoutedEventArgs()}:Array.Empty<object?>());
            var watch=Stopwatch.StartNew();var activeEveryFrame=true;var mixed=0;var washMixed=0;
            while(watch.Elapsed.TotalMilliseconds<310)
            {
                await Task.Delay(16);var compact=Field<FrameworkElement>("CompactPanel");var details=Field<FrameworkElement>("DetailsPanel");
                activeEveryFrame&=Active();if(compact.Opacity>.01&&compact.Opacity<.99&&details.Opacity>.01&&details.Opacity<.99)mixed++;
                var wash=Field<FrameworkElement>("GlassPageWash");if(wash.IsVisible&&wash.Opacity>.01&&wash.Opacity<.99)washMixed++;
                samples.Add(new{expand,atMs=watch.Elapsed.TotalMilliseconds,native=Active(),compact=compact.Opacity,details=details.Opacity,captures=Captures()});
            }
            check(expand?"opening navigation keeps every sampled frame on the same native plane":"returning home keeps every sampled frame on the same native plane",activeEveryFrame&&ReferenceEquals(surface,Field<object>("nativeForegroundSurface")),new{mixed,captures=Captures()});
            check(expand?"navigation opening preserves intermediate slide/fade stages":"navigation return preserves intermediate slide/fade stages",mixed>=2,new{mixed});
            check(expand?"glass navigation wash fades in with the page":"glass navigation wash fades out instead of switching off on return",washMixed>=2,new{washMixed});
        }
        await Navigation(true);
        var progress=Field<PixelProgress>("GoalProgress");
        var scan=typeof(PixelProgress).GetField("nativeScan",Flags)!.GetValue(progress)!;
        var opacity=(float)scan.GetType().GetProperty("PresentationOpacity",Flags)!.GetValue(scan)!;
        check("navigation keeps the actual native composition root visible while progress is collapsed",opacity>.99f,new{opacity});
        var navCaptures=Captures();await Task.Delay(130);
        check("settled navigation stops finite frame uploads",Active()&&Captures()==navCaptures&&!Field<DispatcherTimer>("statusAnimationTimer").IsEnabled,new{navCaptures,after=Captures()});
        await Navigation(false);
        var compactCaptures=Captures();await Task.Delay(130);
        check("settled home stops finite frame uploads",Active()&&Captures()==compactCaptures&&!Field<DispatcherTimer>("statusAnimationTimer").IsEnabled,new{compactCaptures,after=Captures()});
        for(var i=0;i<12;i++){Call("ToggleExpanded",null,new RoutedEventArgs());await Task.Delay(35);Call("CollapseToTimer");await Task.Delay(280);}
        check("rapid reversed navigation reuses one foreground surface and settles home",Active()&&ReferenceEquals(surface,Field<object>("nativeForegroundSurface"))&&!Field<bool>("isExpanded")&&Field<object?>("rootTransition")==null,null);
        var planes=new[]{"nativeNavigationCompact","nativeNavigationDetails","nativeNavigationWash"}.Select(name=>typeof(IslandWindow).GetField(name,Flags)?.GetValue(window)).ToArray();
        check("completed reversed navigation detaches all one-shot composition controllers",planes.All(p=>p is not null&&(int)p.GetType().GetProperty("AnimationReferences",Flags)!.GetValue(p)! == 0)&&!Field<bool>("nativeNavigationActive"),null);
        Call("ToggleExpanded",null,new RoutedEventArgs());await Task.Delay(310);Call("OpenSettings",window,new RoutedEventArgs());await Task.Delay(310);
        check("settings editing still uses live WPF",!Active()&&Field<Border>("OuterBorder").Opacity==1,null);
        Call("CollapseToTimer");await Task.Delay(420);check("return from an editable subpage restores the ready compact renderer",Active(),null);
        settings.ReduceMotion=true;Call("Render");await Task.Delay(70);
        check("reduced motion retains static WPF without a continuous upload timer",!Active()&&!Field<DispatcherTimer>("statusAnimationTimer").IsEnabled,null);
        File.WriteAllText(Path.Combine(output,"navigation-handoff.json"),JsonSerializer.Serialize(samples,AtomicStore.JsonOptions));
        if(live)
        {
            settings.ReduceMotion=false;Call("Render");await Task.Delay(450);
            window.Title="Work Island · 闪烁验收 · "+RuntimeCompat.ProcessId;window.ShowInTaskbar=true;window.Topmost=true;
            var guard=new Window{Title="Work Island · 外部点击验收 · "+RuntimeCompat.ProcessId,Width=540,Height=220,Left=100,Top=80,Background=System.Windows.Media.Brushes.White,Content=new TextBlock{Text="独立点击区域",Margin=new Thickness(30)}};guard.Show();
            void Record(string action)=>File.AppendAllText(Path.Combine(output,"actual-input.jsonl"),JsonSerializer.Serialize(new{action,at=DateTimeOffset.Now,native=Active(),captures=Captures(),expanded=Field<bool>("isExpanded")} )+Environment.NewLine);
            window.Activated+=(_,_)=>Record("activated");window.Deactivated+=(_,_)=>Record("deactivated");
            window.PreviewMouseUp+=(_,_)=>window.Dispatcher.BeginInvoke(()=>Record("mouse-up"),DispatcherPriority.Background);
            File.WriteAllText(Path.Combine(output,"ready.json"),JsonSerializer.Serialize(new{pid=RuntimeCompat.ProcessId,title=window.Title,guard=guard.Title}));
            try{var watch=Stopwatch.StartNew();while(watch.Elapsed.TotalSeconds<180&&!File.Exists(Path.Combine(output,"finish-review.txt")))await Task.Delay(200);}
            finally{guard.Close();}
        }
    }
}
