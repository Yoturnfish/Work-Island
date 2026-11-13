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
using System.ComponentModel;
using System.Threading;
using Drawing=System.Drawing;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunNavigationSmoothness(IslandWindow window,string output,Action<string,bool,object?> check,bool live=false)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;
        settings.HasCompletedOnboarding=true;settings.ReduceMotion=false;settings.BackgroundStyle="glass";settings.BackgroundOpacity=1;settings.SuperReminderEnabled=false;settings.Rules=[new("smooth.exe","Fixture")];
        Field<DispatcherTimer>("fullscreenTimer").Stop();window.Left=360;window.Top=300;Call("ApplyMaterial");Call("NotifyPointer",false);
        engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new("smooth.exe","Fixture",0));Call("Render");await Task.Delay(450);
        if(typeof(IslandWindow).GetMethod("EnableLiveForeground",Flags) is {} liveInit)liveInit.Invoke(window,null);
        else{Call("EnableNativeForeground");Call("EnableNativePulses");}
        await Task.Delay(180);
        check("ordinary presentation has one live foreground instead of a whole-window snapshot controller",!Field<bool>("nativeForegroundEnabled")&&Field<int>("nativeForegroundCaptures")==0, null);
        var guard=new Window{Title="Work Island · 页面帧采样背景",Left=320,Top=240,Width=500,Height=220,Topmost=true,Background=System.Windows.Media.Brushes.White};guard.Show();window.Topmost=true;window.Activate();window.Closed+=(_,_)=>guard.Close();await Task.Delay(80);
        var border=Field<Border>("OuterBorder");var visibleRootWrites=0;
        var descriptor=DependencyPropertyDescriptor.FromProperty(UIElement.OpacityProperty,typeof(Border));
        EventHandler rootChanged=(_,_)=>{if(Field<bool>("nativeForegroundActive")&&border.Opacity>0)visibleRootWrites++;};descriptor.AddValueChanged(border,rootChanged);
        var rows=new List<object>();
        async Task Sample(bool expand,string? page=null)
        {
            var origin=window.PointToScreen(new Point());var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(window);
            var width=(int)Math.Round(window.ActualWidth*dpi.DpiScaleX);var height=(int)Math.Round(window.ActualHeight*dpi.DpiScaleY);
            var frames=new List<(Drawing.Bitmap Image,double At,double Luma,double Center,int Green)>();var frameWatch=Stopwatch.StartNew();var stop=0;
            var sampler=Task.Run(()=>
            {
                using var image=new Drawing.Bitmap(width,height);using var graphics=Drawing.Graphics.FromImage(image);
                while(Volatile.Read(ref stop)==0)
                {
                    graphics.CopyFromScreen((int)Math.Round(origin.X),(int)Math.Round(origin.Y),0,0,image.Size);
                    var luma=0d;for(var x=width/2-4;x<=width/2+4;x+=2){var c=image.GetPixel(x,Math.Max(1,(int)(height*.10)));luma+=(c.R+c.G+c.B)/3d;}
                    var count=0;var sum=0d;for(var y=height/5;y<height*4/5;y+=2)for(var x=0;x<width;x+=2){var c=image.GetPixel(x,y);if(c.G>c.R+60&&c.G>c.B+30){count++;sum+=x;}}
                    frames.Add(((Drawing.Bitmap)image.Clone(),frameWatch.Elapsed.TotalMilliseconds,luma/5,count==0?0:sum/count,count));Thread.Sleep(4);
                }
            });
            await Task.Delay(40);
            var before=Field<int>("nativeForegroundCaptures");var beforeCpu=Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds;
            var watch=Stopwatch.StartNew();
            if(page=="settings")Call("OpenSettings",window,new RoutedEventArgs());
            else if(page=="navigation")Call("BackToNavigation",window,new RoutedEventArgs());
            else Call(expand?"ToggleExpanded":"CollapseToTimer",expand?new object?[]{null,new RoutedEventArgs()}:Array.Empty<object?>());
            var setupMs=watch.Elapsed.TotalMilliseconds;
            var ticking=false;var gaps=new List<double>();var prior=watch.Elapsed.TotalMilliseconds;
            while(watch.Elapsed.TotalMilliseconds<330)
            {
                await Task.Delay(4);var at=watch.Elapsed.TotalMilliseconds;gaps.Add(at-prior);prior=at;
                ticking|=Field<DispatcherTimer>("statusAnimationTimer").IsEnabled;
            }
            var captures=Field<int>("nativeForegroundCaptures")-before;var cpu=Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds-beforeCpu;
            await Task.Delay(60);Volatile.Write(ref stop,1);await sampler;
            var folder=Path.Combine(output,page is null?(expand?"opening-screen":"return-screen"):page+"-screen");Directory.CreateDirectory(folder);
            try
            {
                var first=frames.Take(3).Average(f=>f.Luma);var last=frames.TakeLast(3).Average(f=>f.Luma);var min=frames.Min(f=>f.Luma);var max=frames.Max(f=>f.Luma);
                var positions=frames.Where(f=>f.Green>10).Select(f=>Math.Round(f.Center)).Distinct().Count();
                check((page??(expand?"opening":"returning"))+" backdrop has no blank or doubled-opacity flash",min>=Math.Min(first,last)-12&&max<=Math.Max(first,last)+12,new{frames=frames.Count,first,last,min,max,positions});
                if(page is null)check(expand?"opening page presents more than seven distinct screen movement positions":"returning page presents more than seven distinct screen movement positions",positions>7,new{positions});
                for(var i=0;i<frames.Count;i++)frames[i].Image.Save(Path.Combine(folder,$"frame-{i:000}.png"));
                File.WriteAllText(Path.Combine(folder,"frames.json"),JsonSerializer.Serialize(frames.Select((f,i)=>new{file=$"frame-{i:000}.png",atMs=f.At,luma=f.Luma,center=f.Center,green=f.Green}),AtomicStore.JsonOptions));
            }
            finally{foreach(var f in frames)f.Image.Dispose();}
            rows.Add(new{expand,page,setupMs,captures,cpuSeconds=cpu,ticking,maximumDispatcherGapMs=gaps.Max(),gaps});
            check((page??(expand?"opening":"returning"))+" page has no software snapshot frame timer",!ticking,new{captures,setupMs});
            check((page??(expand?"opening":"returning"))+" page uses bounded static captures",captures<=5,new{captures});
        }
        await Sample(true);await Sample(true,"settings");await Sample(true,"navigation");await Sample(false);
        descriptor.RemoveValueChanged(border,rootChanged);
        check("native page captures never expose the live root between frames",visibleRootWrites==0,new{visibleRootWrites});
        settings.BackgroundStyle="black";settings.BackgroundOpacity=.65;Call("ApplyMaterial");Call("Render");await Task.Delay(100);
        double Opacity()=>Field<bool>("nativeForegroundEnabled")?Field<double>("nativeLogicalOpacity"):window.Opacity;
        Call("NotifyPointer",true);var entered=Opacity();Call("NotifyPointer",false);var left=Opacity();
        check("pointer enter and leave retain the manually selected opacity",Math.Abs(entered-.65)<.001&&Math.Abs(left-.65)<.001,new{entered,left});
        File.WriteAllText(Path.Combine(output,"navigation-performance.json"),JsonSerializer.Serialize(rows,AtomicStore.JsonOptions));
        if(live)
        {
            window.Title="Work Island · 页面流畅度验收 · "+Environment.ProcessId;window.ShowInTaskbar=true;guard.Title="Work Island · 外部点击验收 · "+Environment.ProcessId;
            guard.Topmost=false;
            void Record(string action)=>File.AppendAllText(Path.Combine(output,"actual-input.jsonl"),JsonSerializer.Serialize(new{action,at=DateTimeOffset.Now,native=Field<bool>("nativeForegroundActive"),captures=Field<int>("nativeForegroundCaptures"),opacity=Opacity(),expanded=Field<bool>("isExpanded")})+Environment.NewLine);
            window.Activated+=(_,_)=>Record("activated");window.Deactivated+=(_,_)=>Record("deactivated");
            window.PreviewMouseUp+=(_,_)=>window.Dispatcher.BeginInvoke(()=>Record("mouse-up"),DispatcherPriority.Background);
            File.WriteAllText(Path.Combine(output,"ready.json"),JsonSerializer.Serialize(new{pid=Environment.ProcessId,title=window.Title,guard=guard.Title}));
            var wait=Stopwatch.StartNew();while(wait.Elapsed.TotalSeconds<180&&!File.Exists(Path.Combine(output,"finish-review.txt")))await Task.Delay(200);
        }
    }
}
