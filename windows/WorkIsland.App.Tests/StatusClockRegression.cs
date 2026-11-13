using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;
using Color=System.Drawing.Color;

internal static partial class Program
{
    private static byte[] GlyphBytes(Bitmap bitmap){using var stream=new MemoryStream();bitmap.Save(stream,System.Drawing.Imaging.ImageFormat.Png);return stream.ToArray();}
    private static int LitPixels(Bitmap bitmap)
    {
        var count=0;for(var y=0;y<bitmap.Height;y++)for(var x=0;x<bitmap.Width;x++){var radius=Math.Sqrt(Math.Pow(x+.5-bitmap.Width/2d,2)+Math.Pow(y+.5-bitmap.Height/2d,2))/bitmap.Width;var c=bitmap.GetPixel(x,y);if(radius is >=.32 and <=.445&&c.A>150&&c.G>200&&c.G-c.R>65&&c.G-c.B>65)count++;}return count;
    }
    private static void RunStatusClockArtworkChecks(string output,Action<string,bool,object?> check)
    {
        using var art=new StatusGlyphArtwork();
        foreach(var size in new[]{16,20,24,32,64})
        {
            var counts=new List<int>();
            foreach(var progress in new[]{0,.25,.5,.75,1})
            {
                using var image=art.Render("tracking","green",progress,size);counts.Add(LitPixels(image));image.Save(Path.Combine(output,$"clock-{size}-{(int)(progress*100):000}.png"));
            }
            check($"{size}px continuous accent arc grows monotonically across goal percentages",counts.Zip(counts.Skip(1)).All(p=>p.First<=p.Second)&&counts[1]>counts[0]&&counts[2]>counts[1]&&counts[4]>counts[3],counts);
        }
        using(var a=art.Render("tracking","green",.5,32,scanPhase:.2))
        using(var b=art.Render("tracking","green",.5,32,scanPhase:.6))
        {
            var innerStable=true;var ringChanged=false;var exteriorClear=true;
            for(var y=0;y<32;y++)for(var x=0;x<32;x++)
            {var radius=Math.Sqrt(Math.Pow(x+.5-16,2)+Math.Pow(y+.5-16,2));if(radius<32*.20)innerStable&=a.GetPixel(x,y)==b.GetPixel(x,y);else if(radius<32*.46)ringChanged|=a.GetPixel(x,y)!=b.GetPixel(x,y);if(radius>=32*.50)exteriorClear&=a.GetPixel(x,y).A==0&&b.GetPixel(x,y).A==0;}
            check("light cycles inside the ordinary clock with stable hands and no added outer ring",innerStable&&ringChanged&&exteriorClear,new{innerStable,ringChanged,exteriorClear,first=LitPixels(a),second=LitPixels(b)});
        }
        using(var zero=art.Render("tracking","green",0,64))
        using(var half=art.Render("tracking","green",.5,64))
        using(var full=art.Render("tracking","green",1,64))
        {
            Color At(Bitmap image,double degrees){var radians=degrees*Math.PI/180;return image.GetPixel((int)Math.Floor(32+Math.Cos(radians)*64*.39),(int)Math.Floor(32+Math.Sin(radians)*64*.39));}
            bool Bright(Color c)=>c.A>200&&c.G>200;
            check("half progress is solid clockwise from twelve while remaining half retains its base color",Enumerable.Range(0,8).All(n=>Bright(At(half,-80+n*20)))&&Enumerable.Range(0,8).All(n=>At(half,100+n*20)==At(zero,100+n*20)),null);
            check("completion forms a continuous solid ring including former dot gaps",Enumerable.Range(0,36).All(n=>Bright(At(full,n*10+5))),null);
            check("normal clock has a continuous face without pixel gaps and transparent corners",Enumerable.Range(0,36).All(n=>At(zero,n*10+5).A>200&&At(zero,n*10+5).G>90)&&zero.GetPixel(32,45).ToArgb()==Color.Black.ToArgb()&&zero.GetPixel(0,0).A==0&&half.GetPixel(63,63).A==0,null);
        }
        using(var a=art.Render("tracking","green",0,32,scanPhase:.2))
        using(var b=art.Render("tracking","green",0,32,scanPhase:.6))
        using(var c=art.Render("tracking","green",1,32,scanPhase:.2))
        using(var d=art.Render("tracking","green",1,32,scanPhase:.6))
            check("clock circle keeps flowing at both zero and full completion",!GlyphBytes(a).SequenceEqual(GlyphBytes(b))&&!GlyphBytes(c).SequenceEqual(GlyphBytes(d)),null);
        using(var a=art.Render("away","green",0,32))
        using(var b=art.Render("away","green",1,32))
        {check("away cup does not turn into a progress clock at another goal percentage",GlyphBytes(a).SequenceEqual(GlyphBytes(b)),null);a.Save(Path.Combine(output,"coffee-32.png"));}
        using(var paused=art.Render("paused","green",.5,32)){paused.Save(Path.Combine(output,"paused-32.png"));check("pause remains a distinct symbol",!GlyphBytes(paused).SequenceEqual(File.ReadAllBytes(Path.Combine(output,"coffee-32.png"))),null);}
        using(var a=art.Render("tracking","green",double.NaN,32))
        using(var b=art.Render("tracking","green",0,32))check("invalid progress cannot create a filled clock",GlyphBytes(a).SequenceEqual(GlyphBytes(b)),null);
        var motion=new StatusItemMotion();var green=Color.FromArgb(64,255,97);var orange=Color.FromArgb(255,149,0);
        motion.Retarget(green,"tracking",.5,false,0,null);motion.Retarget(orange,"away",.5,false,1,0);
        var mid=motion.GlyphAt(1.175);
        check("clock and coffee crossfade at the existing 350ms midpoint",Math.Abs(mid.Clock-.5)<.00001&&Math.Abs(mid.Coffee-.5)<.00001&&mid.Pause==0&&motion.IsGlyphTransitioning(1.175),mid);
        motion.Retarget(green,"tracking",.5,false,1.175,null);
        check("rapid symbol reversal starts at the visible mixture",motion.GlyphAt(1.175)==mid,null);
        motion.Retarget(orange,"away",.5,true,1.2,0);
        check("reduced motion settles the symbol and stops all frame work",motion.GlyphAt(1.2)==new StatusGlyphBlend(0,1,0)&&!motion.NeedsFrames(1.2),null);
        motion.Retarget(green,"tracking",.5,false,2,null);
        check("pixel clock shimmer retains the existing 2.8 second cycle",Math.Abs(motion.ScanPhase(2.7)-motion.ScanPhase(5.5))<.00001,null);
        var frames=Path.Combine(output,"clock-cycle");Directory.CreateDirectory(frames);
        for(var i=0;i<68;i++){using var image=art.Render("tracking","green",.65,32,scanPhase:i/68d);image.Save(Path.Combine(frames,$"frame-{i:000}.png"));}
        File.WriteAllText(Path.Combine(output,"clock-artwork.json"),JsonSerializer.Serialize(new{scope="DPI-size raster and visual-pixel assertions; fixed explicit phase frames are an illustration, not Explorer capture. Actual production Shell submission and native state controls are separate.",cycleSeconds=2.8,transitionSeconds=.35},AtomicStore.JsonOptions));
        RunStatusAlignmentChecks(output,check);
    }

    private static async Task RunStatusClockLive(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethods(Flags).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;
        settings.Paused=false;settings.ReduceMotion=false;settings.Rules=[new("clock-review.exe","Clock review")];settings.GoalSeconds=7200;
        Field<DispatcherTimer>("fullscreenTimer").Stop();Field<DispatcherTimer>("timer").Stop();
        var now=DateTimeOffset.Now;engine.Data.Sessions.Clear();
        engine.Data.Sessions.Add(new Session{ApplicationIdentifier="clock-review.exe",ApplicationName="Clock review",DayKey=LogicalDay.Key(now,engine.Zone,settings.DayResetMinutes),StartedAt=now.AddHours(-1),EndedAt=now,EffectiveSeconds=3600});
        engine.ResetObservation();engine.Observe(now,new("clock-review.exe","Clock review",0));Call("ChangeDisplayMode","menuBar");Call("Render");
        var native=Field<object>("statusItem");var frames=0;var snapshots=new List<object>();var clicks=new List<string>();
        var image=new System.Windows.Controls.Image{Width=160,Height=160,Stretch=Stretch.Uniform,Margin=new Thickness(20)};
        RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.NearestNeighbor);
        var label=new TextBlock{Text="实际提交到系统托盘的同一帧 · 放大查看",Margin=new Thickness(12),TextAlignment=TextAlignment.Center};
        var nativeImage=new System.Windows.Controls.Image{Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center,SnapsToDevicePixels=true,UseLayoutRounding=true};
        RenderOptions.SetBitmapScalingMode(nativeImage,BitmapScalingMode.NearestNeighbor);
        var rows=new StackPanel{Background=new SolidColorBrush(System.Windows.Media.Color.FromRgb(29,33,30))};rows.Children.Add(label);rows.Children.Add(image);rows.Children.Add(new TextBlock{Text="托盘实际像素大小",TextAlignment=TextAlignment.Center,Margin=new Thickness(0,0,0,6)});rows.Children.Add(nativeImage);
        var controls=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(12)};rows.Children.Add(controls);
        var gallery=new Window{Title="Work Island · 单格时钟验收 · "+Environment.ProcessId,Width=420,Height=410,WindowStartupLocation=WindowStartupLocation.CenterScreen,Content=rows,Foreground=System.Windows.Media.Brushes.White,ShowInTaskbar=true,Topmost=false,UseLayoutRounding=true};
        Action<Bitmap> submitted=bitmap=>
        {
            frames++;var bytes=GlyphBytes(bitmap);using var stream=new MemoryStream(bytes);var source=new BitmapImage();source.BeginInit();source.CacheOption=BitmapCacheOption.OnLoad;source.StreamSource=stream;source.EndInit();source.Freeze();image.Source=source;
            var dpi=VisualTreeHelper.GetDpi(gallery);nativeImage.Width=bitmap.Width/dpi.DpiScaleX;nativeImage.Height=bitmap.Height/dpi.DpiScaleY;nativeImage.Source=source;
            if(frames%12==1){var file=$"live-{frames:0000}.png";File.WriteAllBytes(Path.Combine(output,file),bytes);snapshots.Add(new{file,state=engine.Current.State,at=DateTimeOffset.Now,size=bitmap.Width});}
        };
        native.GetType().GetEvent("FrameSubmitted")!.AddEventHandler(native,submitted);
        void Choose(string state,double progress,string text)
        {
            engine.ResetObservation();settings.Paused=state=="paused";engine.Data.Sessions[0].EffectiveSeconds=progress*7200;
            engine.Observe(DateTimeOffset.Now,state=="tracking"?new("clock-review.exe","Clock review",0):new("unmatched.exe","Unmatched",0));
            Call("Render");clicks.Add(text);File.WriteAllText(Path.Combine(output,"live-actions.json"),JsonSerializer.Serialize(new{clicks,state=engine.Current.State,progress=engine.Progress(DateTimeOffset.Now)},AtomicStore.JsonOptions));
        }
        foreach(var item in new[]{("计时 50%","tracking",.5),("游离咖啡","away",.5),("暂停","paused",.5),("完成 100%","tracking",1d)})
        {var button=new Button{Content=item.Item1,Margin=new Thickness(3),Padding=new Thickness(6),Foreground=System.Windows.Media.Brushes.Black};button.Click+=(_,_)=>Choose(item.Item2,item.Item3,item.Item1);controls.Children.Add(button);}
        try
        {
            gallery.Show();Call("Render");
            using var process=Process.GetCurrentProcess();
            File.WriteAllText(Path.Combine(output,"ready.json"),JsonSerializer.Serialize(new{pid=process.Id,startedUtcTicks=process.StartTime.ToUniversalTime().Ticks.ToString(),title=gallery.Title,scope="Own gallery displays the exact production Shell submission bytes; controls invoke controlled product states in isolated data. Not a screenshot or physical click of Explorer's icon."},AtomicStore.JsonOptions));
            var watch=Stopwatch.StartNew();while(!File.Exists(Path.Combine(output,"finish.txt"))&&watch.Elapsed.TotalSeconds<300)await Task.Delay(100);
            check("actual gallery controls exercised clock coffee pause and completion",new[]{"计时 50%","游离咖啡","暂停","完成 100%"}.All(clicks.Contains),clicks);
            check("native production icon submissions remained a single installed Shell item",frames>0&&(bool)native.GetType().GetProperty("Installed")!.GetValue(native)!,new{frames});
            File.WriteAllText(Path.Combine(output,"live-frames.json"),JsonSerializer.Serialize(new{frames,snapshots,clicks,scope="Exact Shell submission buffers; official gallery input, not direct Explorer icon acceptance."},AtomicStore.JsonOptions));
        }
        finally{native.GetType().GetEvent("FrameSubmitted")!.RemoveEventHandler(native,submitted);gallery.Close();Call("ChangeDisplayMode","floating");Field<DispatcherTimer>("statusAnimationTimer").Stop();Field<DispatcherTimer>("focusReturn").Stop();}
    }
}
