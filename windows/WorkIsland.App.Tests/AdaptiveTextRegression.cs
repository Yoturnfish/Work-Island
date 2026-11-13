using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    [DllImport("user32.dll",EntryPoint="GetGuiResources")]private static extern uint AdaptiveGuiResources(IntPtr process,uint flags);
    private static async Task RunAdaptiveTextChecks(IslandWindow window,string output,Action<string,bool,object?> check,bool review)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var type=typeof(IslandWindow).Assembly.GetType("WorkIsland.App.TextContrastPolicy");
        check("adaptive text contrast policy exists",type is not null,null);if(type is null)return;
        var policy=Activator.CreateInstance(type,true)!;var member=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        void Observe(double l)=>type.GetMethod("Observe",member)!.Invoke(policy,[l]);
        bool Dark()=> (bool)type.GetProperty("DarkInk",member)!.GetValue(policy)!;
        void Reset()=>type.GetMethod("Reset",member)!.Invoke(policy,null);
        Observe(.95);check("initial bright background uses dark ink",Dark(),null);
        Observe(.18);check("threshold deadband holds current polarity",Dark(),null);
        Observe(.05);check("one dark frame cannot flip text",Dark(),null);
        Observe(.05);check("two sustained dark samples restore white",!Dark(),null);
        Observe(.9);Observe(.2);Observe(.9);check("interrupted bright samples cannot accumulate",!Dark(),null);
        Observe(.9);check("two sustained bright samples darken text",Dark(),null);
        Observe(double.NaN);Observe(double.PositiveInfinity);Observe(-1);check("invalid samples cannot change polarity",Dark(),null);
        Reset();check("black material reset restores white",!Dark()&&(Color)type.GetProperty("Title",member)!.GetValue(policy)! == Colors.White,null);
        var engine=Field<TrackingEngine>("engine");engine.Data.Settings.Paused=false;engine.Data.Settings.BackgroundStyle="glass";engine.Data.Settings.ReduceMotion=false;engine.ResetObservation();
        window.ShowInTaskbar=true;window.Left=180;window.Top=240;window.Topmost=true;
        var background=new Window{Title="Work Island · Text contrast test background",Left=100,Top=140,Width=800,Height=500,ShowActivated=false,Background=Brushes.White};
        background.Show();window.Topmost=true;Call("ApplyMaterial");Call("EnableNativeForeground");Call("EnableNativePulses");
        var samples=new List<object>();
        int Count()=>Field<object?>("textBackdropSampler") is {} sampler?(int)sampler.GetType().GetProperty("Samples",member)!.GetValue(sampler)!:0;
        bool LiveDark()=> (bool)type.GetProperty("DarkInk",member)!.GetValue(Field<object>("textContrast"))!;
        async Task Sample(string? curveName=null)
        {
            typeof(IslandWindow).GetField("lastContrastSample",Flags)!.SetValue(window,0L);Call("Render");
            if(curveName is null){await Task.Delay(430);return;}
            var curve=new List<(double ms,byte red,bool clock,bool native)>();var watch=Stopwatch.StartNew();
            while(watch.Elapsed.TotalMilliseconds<430){curve.Add((watch.Elapsed.TotalMilliseconds,Field<SolidColorBrush>("compactTitleInk").Color.R,typeof(IslandWindow).GetField("compactInkMotion",Flags)?.GetValue(window)!=null,Field<bool>("nativeForegroundActive")));await Task.Delay(16);}
            var rows=curve.Select(p=>new{p.ms,p.red,p.clock,p.native}).ToArray();
            File.WriteAllText(Path.Combine(output,curveName+".json"),JsonSerializer.Serialize(new{rows,durationSeconds=.35,scope="Actual independent WPF ink color clock sampled at wall-clock times. Not screen frames or a compositor recording."},AtomicStore.JsonOptions));
            check("contrast fade has actual intermediate values and releases native foreground",curve.Any(p=>p.ms>60&&p.ms<300&&p.red>40&&p.red<250)&&Field<object?>("stateTransition")==null&&Field<bool>("nativeForegroundActive"),rows);
        }
        async Task Stage(string name)
        {
            var title=Field<TextBlock>("TitleLabel");var time=Field<TextBlock>("TimeLabel");
            var sample=new{name,dark=LiveDark(),title=((SolidColorBrush)title.Foreground).Color,time=((SolidColorBrush)time.Foreground).Color,count=Count(),native=Field<bool>("nativeForegroundActive"),lastMs=Field<object?>("textBackdropSampler")?.GetType().GetProperty("LastMilliseconds",member)!.GetValue(Field<object>("textBackdropSampler"))};samples.Add(sample);
            File.WriteAllText(Path.Combine(output,"contrast-stages.json"),JsonSerializer.Serialize(samples,AtomicStore.JsonOptions));
            if(!review)return;
            File.WriteAllText(Path.Combine(output,"review-stage.json"),JsonSerializer.Serialize(new{stage=name,processId=Environment.ProcessId,window=window.Title},AtomicStore.JsonOptions));
            var wait=Stopwatch.StartNew();while(!File.Exists(Path.Combine(output,"continue-"+name+".json"))&&wait.Elapsed.TotalSeconds<180)await Task.Delay(100);
        }
        async Task CheckSemanticTime(string scene)
        {
            var settings=engine.Data.Settings;var originalRules=settings.Rules;
            settings.Rules=[new("semantic-time.exe","Time color fixture")];
            try
            {
                foreach(var state in new[]{"tracking","paused","away"})
                {
                    settings.Paused=state=="paused";engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new(state=="tracking"?"semantic-time.exe":"other.exe","Time color fixture",0));Call("Render");await Task.Delay(430);
                    var expected=state=="tracking"?(Color)typeof(IslandWindow).GetProperty("ThemeColor",member)!.GetValue(window)!:state=="paused"?Color.FromRgb(173,173,173):Color.FromRgb(255,149,0);
                    var actual=(SolidColorBrush)Field<TextBlock>("TimeLabel").Foreground;
                    check(scene+" time retains original "+state+" color",actual.Color==expected,new{expected=expected.ToString(),actual=actual.Color.ToString()});
                }
            }
            finally{settings.Rules=originalRules;settings.Paused=false;engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new("other.exe","Time color fixture",0));Call("Render");}
            await Task.Delay(430);
        }
        try
        {
            await Task.Delay(350);await Sample();
            check("real one-row screen sample sees own white background",LiveDark()&&Count()>0,Field<object>("textContrast"));
            check("bright material darkens only title and preserves original time ink",((SolidColorBrush)Field<TextBlock>("TitleLabel").Foreground).Color.R<60&&((SolidColorBrush)Field<TextBlock>("TimeLabel").Foreground).Color==Field<SolidColorBrush>("statusBrush").Color,null);
            await CheckSemanticTime("white background");
            Field<TextBox>("TitleEditor").Visibility=Visibility.Visible;Field<TextBlock>("TitleLabel").Visibility=Visibility.Collapsed;window.UpdateLayout();Call("Render");
            check("title editor inherits readable dark ink on a bright background",((SolidColorBrush)Field<TextBox>("TitleEditor").Foreground).Color.R<60,null);
            Call("CancelTitleEdit",null,null);Call("Render");await Task.Delay(450);check("closing readable title editor restores native foreground",Field<bool>("nativeForegroundActive"),null);
            await Stage("white");
            var before=Count();var watch=Stopwatch.StartNew();for(var i=0;i<12;i++)Call("Render");
            check("repeated Render calls are rate limited to one sample per second",Count()-before<=Math.Ceiling(watch.Elapsed.TotalSeconds),new{before,after=Count(),ms=watch.Elapsed.TotalMilliseconds});
            background.Background=Brushes.Black;await Task.Delay(250);await Sample();await Sample("contrast-white-to-dark-background");
            check("sustained own dark background restores white",!LiveDark()&&((SolidColorBrush)Field<TextBlock>("TitleLabel").Foreground).Color==Colors.White,null);
            check("dark background preserves original semantic time color",((SolidColorBrush)Field<TextBlock>("TimeLabel").Foreground).Color==Field<SolidColorBrush>("statusBrush").Color,null);
            await CheckSemanticTime("black background");
            await Stage("black");
            var timeStart=Field<TextBlock>("TimeLabel").PointToScreen(new Point());
            var boundary=(timeStart.X/VisualTreeHelper.GetDpi(window).DpiScaleX-background.Left-10)/background.Width;
            var splitBrush=new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(1,0)};
            splitBrush.GradientStops.Add(new(Colors.White,0));splitBrush.GradientStops.Add(new(Colors.White,boundary));splitBrush.GradientStops.Add(new(Colors.Black,boundary));splitBrush.GradientStops.Add(new(Colors.Black,1));background.Background=splitBrush;
            await Task.Delay(250);await Sample();await Sample();
            check("split background darkens title while time retains original state color",LiveDark()&&((SolidColorBrush)Field<TextBlock>("TimeLabel").Foreground).Color==Field<SolidColorBrush>("statusBrush").Color,new{boundary,titleDark=LiveDark()});
            Field<TextBox>("TitleEditor").Visibility=Visibility.Visible;Field<TextBlock>("TitleLabel").Visibility=Visibility.Collapsed;window.UpdateLayout();await Sample();await Sample();
            check("split background editor samples its own left region",LiveDark()&&((SolidColorBrush)Field<TextBox>("TitleEditor").Foreground).Color.R<60,null);Call("CancelTitleEdit",null,null);Call("Render");await Task.Delay(450);
            await Stage("split");
            var livePolicy=Field<object>("textContrast");
            void ForcePolicy(double value){type.GetMethod("Observe",member)!.Invoke(livePolicy,[value]);type.GetMethod("Observe",member)!.Invoke(livePolicy,[value]);typeof(IslandWindow).GetField("lastContrastSample",Flags)!.SetValue(window,Stopwatch.GetTimestamp());}
            ForcePolicy(.01);Call("Render");await Task.Delay(70);var beforeReverse=Field<SolidColorBrush>("compactTitleInk").Color;
            ForcePolicy(.95);Call("Render");var afterReverse=Field<SolidColorBrush>("compactTitleInk").Color;
            check("rapid contrast reversal begins at the presented color",Math.Abs(beforeReverse.R-afterReverse.R)<12,new{beforeReverse,afterReverse});await Task.Delay(450);
            check("rapid contrast reversal settles without a retained clock",Field<SolidColorBrush>("compactTitleInk").Color.R==32&&Field<object?>("stateTransition")==null,null);
            engine.Data.Settings.ReduceMotion=true;ForcePolicy(.01);Call("Render");check("reduced motion applies contrast without adding an animation clock",Field<SolidColorBrush>("compactTitleInk").Color==Colors.White&&Field<object?>("stateTransition")==null,null);
            engine.Data.Settings.ReduceMotion=false;ForcePolicy(.95);Call("Render");await Task.Delay(450);
            var liveSampler=Field<object>("textBackdropSampler");var read=liveSampler.GetType().GetMethod("TryRead",member)!;
            var point=Field<TextBlock>("TitleLabel").PointToScreen(new Point());
            using(var process=Process.GetCurrentProcess())
            {
                var gdiBefore=AdaptiveGuiResources(process.Handle,0);var cpuBefore=process.TotalProcessorTime.TotalSeconds;var watchSamples=Stopwatch.StartNew();var times=new List<double>();var allRead=true;
                for(var i=0;i<64;i++)
                {
                    object?[] arguments=[(int)point.X,(int)point.Y-3,200,0d];
                    allRead&=(bool)read.Invoke(liveSampler,arguments)!;
                    times.Add((double)liveSampler.GetType().GetProperty("LastMilliseconds",member)!.GetValue(liveSampler)!);
                }
                process.Refresh();var cpuSeconds=process.TotalProcessorTime.TotalSeconds-cpuBefore;var gdiAfter=AdaptiveGuiResources(process.Handle,0);
                File.WriteAllText(Path.Combine(output,"sampler-cost.json"),JsonSerializer.Serialize(new{samples=64,allRead,elapsedMs=watchSamples.Elapsed.TotalMilliseconds,cpuSeconds,cpuMsPerSample=cpuSeconds*1000/64,gdiBefore,gdiAfter,wallTimesMs=times,scope="Direct repeated calls to the actual one-row sampler over own known background, with reused bitmap. Isolates sampler cost, does not represent entire preview or long-run leakage."},AtomicStore.JsonOptions));
                check("reused one-row sampler has no per-sample GDI handle growth",allRead&&gdiAfter<=gdiBefore+2,new{gdiBefore,gdiAfter,cpuSeconds});
            }
            foreach(var pair in new[]{("屏幕工作时间","0H 02M"),("Work time","12H 48M"),("工作时间 Screen","123M 45S")})
            {
                var title=Field<TextBlock>("TitleLabel");var time=Field<TextBlock>("TimeLabel");title.Text=pair.Item1;time.Text=pair.Item2;window.UpdateLayout();Call("FitCompactTitle");window.UpdateLayout();Call("AlignCompactTypography");
                FormattedText Measure(TextBlock t)=>new(t.Text,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(t.FontFamily,t.FontStyle,t.FontWeight,t.FontStretch),t.FontSize,Brushes.White,null,TextOptions.GetTextFormattingMode(t),VisualTreeHelper.GetDpi(window).PixelsPerDip);
                double Center(TextBlock t){var ink=Measure(t).BuildGeometry(new Point()).Bounds;return t.TransformToAncestor(window).Transform(new Point(0,ink.Y+ink.Height/2)).Y*VisualTreeHelper.GetDpi(window).DpiScaleY;}
                var error=Math.Abs(Center(title)-Center(time));
                check("visible ink centers align within half pixel "+pair.Item1,error<=.51,new{error,title.FontSize,timeSize=time.FontSize,family=time.FontFamily.Source,weight=time.FontWeight.ToString()});
                check("approved numeric typeface remains Bold14 "+pair.Item1,time.FontFamily.Source=="Consolas"&&time.FontWeight==FontWeights.Bold&&time.FontSize==14,null);
            }
            var previousCount=Count();Call("ToggleExpanded",null,new RoutedEventArgs());await Sample();check("expanded pages perform no background read",Count()==previousCount,null);
            Call("CollapseToTimer");await Task.Delay(300);window.Hide();previousCount=Count();await Sample();check("hidden island performs no background read",Count()==previousCount,null);window.Show();
            typeof(IslandWindow).GetField("fullscreenSuppressed",Flags)!.SetValue(window,true);previousCount=Count();await Sample();check("fullscreen suppression performs no background read",Count()==previousCount,null);typeof(IslandWindow).GetField("fullscreenSuppressed",Flags)!.SetValue(window,false);
            typeof(IslandWindow).GetField("suspended",Flags)!.SetValue(window,true);previousCount=Count();await Sample();check("session suspension performs no background read",Count()==previousCount,null);typeof(IslandWindow).GetField("suspended",Flags)!.SetValue(window,false);
            engine.Data.Settings.BackgroundStyle="black";Call("ApplyMaterial");previousCount=Count();await Sample();check("black material resets ink and performs no read",!LiveDark()&&Count()==previousCount,null);
            check("finite contrast transition releases state and ink clocks",Field<object?>("stateTransition")==null&&typeof(IslandWindow).GetField("compactInkMotion",Flags)?.GetValue(window)==null,null);
        }
        finally{background.Close();}
    }
}
