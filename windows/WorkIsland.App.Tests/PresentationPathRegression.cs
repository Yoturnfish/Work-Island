using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;
using Color=System.Drawing.Color;

internal static partial class Program
{
    private static async Task RunPresentationPathChecks(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethods(Flags).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;
        Field<DispatcherTimer>("timer").Stop();Field<DispatcherTimer>("fullscreenTimer").Stop();
        settings.Rules=[new("path-fixture.exe","Path fixture")];settings.ReduceMotion=false;settings.BackgroundStyle="black";settings.BackgroundOpacity=1;settings.Paused=false;
        window.Left=340;window.Top=240;window.Topmost=true;window.ShowInTaskbar=true;window.Title="Work Island · 正式绘制路径验收 · "+RuntimeCompat.ProcessId;
        var guard=new Window{Title="Work Island owned transition background",WindowStyle=WindowStyle.None,ShowActivated=false,ShowInTaskbar=false,Left=300,Top=200,Width=600,Height=260,Background=System.Windows.Media.Brushes.White};guard.Show();
        void State(bool working){engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new(working?"path-fixture.exe":"other.exe","Fixture",0));Call("Render");}
        bool NativeInstalled()=>Field<object>("statusItem").GetType().GetProperty("Installed")!.GetValue(Field<object>("statusItem")) is true;
        bool Tray()=>Field<System.Windows.Forms.NotifyIcon>("tray").Visible;
        try
        {
            Call("ApplyMaterial");Call("NotifyPointer",false);State(true);await Task.Delay(550);Call("EnableNativeForeground");Call("EnableNativePulses");await Task.Delay(180);
            check("ordinary native floating renderer is enabled for the real transition path",Field<bool>("nativeForegroundActive"),null);
            check("visible floating mode has no competing state tray or menu entry",window.IsVisible&&!Tray()&&!NativeInstalled(),new{window.IsVisible,tray=Tray(),native=NativeInstalled()});
            var element=Field<FrameworkElement>("StatusIcon");var origin=element.PointToScreen(new System.Windows.Point());var dpi=VisualTreeHelper.GetDpi(window);var width=(int)Math.Ceiling(element.ActualWidth*dpi.DpiScaleX);var height=(int)Math.Ceiling(element.ActualHeight*dpi.DpiScaleY);
            var frameFolder=Path.Combine(output,"floating-transition");Directory.CreateDirectory(frameFolder);var samples=new List<object>();var inkCounts=new List<int>();var stop=0;var watch=Stopwatch.StartNew();
            var sampler=Task.Run(()=>
            {
                using var bitmap=new Bitmap(width,height);using var graphics=Graphics.FromImage(bitmap);
                while(Volatile.Read(ref stop)==0)
                {
                    graphics.CopyFromScreen((int)Math.Floor(origin.X),(int)Math.Floor(origin.Y),0,0,bitmap.Size);
                    var ink=0;for(var y=0;y<height;y++)for(var x=0;x<width;x++){var c=bitmap.GetPixel(x,y);if(Math.Max(c.R,Math.Max(c.G,c.B))-Math.Min(c.R,Math.Min(c.G,c.B))>80)ink++;}
                    var file=$"frame-{inkCounts.Count:0000}.png";bitmap.Save(Path.Combine(frameFolder,file));inkCounts.Add(ink);samples.Add(new{file,atMs=watch.Elapsed.TotalMilliseconds,coloredPixels=ink});Thread.Sleep(5);
                }
            });
            var opacities=new List<object>();
            try
            {
                await Task.Delay(100);var switchAt=watch.Elapsed.TotalMilliseconds;State(false);
                // Bound an ordinary UI-thread delay to expose a renderer handoff
                // gap; independent own-window screen sampling continues.
                Thread.Sleep(80);
                for(var i=0;i<12;i++){await Task.Delay(35);opacities.Add(new{atMs=watch.Elapsed.TotalMilliseconds,clock=Field<FrameworkElement>("ClockIcon").Opacity,coffee=Field<FrameworkElement>("CoffeeIcon").Opacity,native=Field<bool>("nativeForegroundActive")});}
                await Task.Delay(120);Volatile.Write(ref stop,1);await sampler;
                var settled=inkCounts.Take(5).Average();check("actual floating icon never blanks across native state handoff",inkCounts.Count>20&&inkCounts.Min()>settled*.35,new{frames=inkCounts.Count,minimum=inkCounts.Min(),initial=settled,switchAt});
                var mixed=opacities.Count(sample=>{var p=sample.GetType();var a=(double)p.GetProperty("clock")!.GetValue(sample)!;var b=(double)p.GetProperty("coffee")!.GetValue(sample)!;return a>.05&&a<.95&&b>.05&&b<.95;});
                check("floating transition presents multiple mixed clock coffee stages",mixed>=2,new{mixed});
                var nativeMixed=opacities.Count(sample=>{var p=sample.GetType();var a=(double)p.GetProperty("clock")!.GetValue(sample)!;return a>.05&&a<.95&&(bool)p.GetProperty("native")!.GetValue(sample)!;});
                check("mixed state frames stay on the active native presentation plane",nativeMixed>=2,new{nativeMixed});
                File.WriteAllText(Path.Combine(output,"floating-transition.json"),JsonSerializer.Serialize(new{frames=samples,opacities,switchAt,scope="Independent screen pixels of own isolated icon rectangle and owned white backdrop; actual normal native controller enabled. Includes a bounded 80ms UI-work stress. Not external app capture or hardware input."},AtomicStore.JsonOptions));
            }
            finally{Volatile.Write(ref stop,1);await sampler;}
            Call("ChangeDisplayMode","menuBar");await Task.Delay(130);
            check("menu mode is the only visible island entry",!window.IsVisible&&!Tray()&&NativeInstalled(),null);
            var native=Field<object>("statusItem");var frames=new List<object>();var frameWatch=Stopwatch.StartNew();var menuFrames=Path.Combine(output,"menu-transition");Directory.CreateDirectory(menuFrames);
            Action<Bitmap> onFrame=bitmap=>{var file=$"frame-{frames.Count:0000}.png";bitmap.Save(Path.Combine(menuFrames,file));frames.Add(new{file,atMs=frameWatch.Elapsed.TotalMilliseconds});};
            var frameEvent=native.GetType().GetEvent("FrameSubmitted")!;frameEvent.AddEventHandler(native,onFrame);
            try{State(true);await Task.Delay(550);}finally{frameEvent.RemoveEventHandler(native,onFrame);}
            check("native menu transition submits intermediate frames while floating stays hidden",frames.Count>=5&&!window.IsVisible&&!Tray()&&NativeInstalled(),new{frames=frames.Count});
            File.WriteAllText(Path.Combine(output,"menu-transition.json"),JsonSerializer.Serialize(frames,AtomicStore.JsonOptions));
            Call("ChangeDisplayMode","floating");await Task.Delay(500);
            check("return to floating removes both menu and spare tray entries",window.IsVisible&&!NativeInstalled()&&!Tray(),null);
            Call("HideIsland",null,new RoutedEventArgs());check("hidden floating keeps a recovery tray entry",!window.IsVisible&&Tray()&&!NativeInstalled(),null);
            Call("ShowIsland");await Task.Delay(120);check("restoring floating removes its recovery tray entry",window.IsVisible&&!Tray()&&!NativeInstalled(),null);
        }
        finally{guard.Close();Field<DispatcherTimer>("focusReturn").Stop();Field<DispatcherTimer>("statusAnimationTimer").Stop();window.Topmost=false;}
    }
}
