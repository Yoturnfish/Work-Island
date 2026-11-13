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
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunReminderGeometryChecks(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        void Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;
        var glow=Field<FrameworkElement>("ReminderGlow");var host=Field<Grid>("PageHost");
        settings.HasCompletedOnboarding=true;settings.Paused=true;settings.Rules=[];settings.Websites=[];
        settings.ReduceMotion=false;settings.SuperReminderEnabled=false;settings.BackgroundStyle="black";
        engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new(null,null,0));Call("Render");
        Call("CollapseToTimer");await Task.Delay(450);window.UpdateLayout();
        var samples=new List<object>();
        double Expected(double x,double y,double strength)
        {
            var r=Math.Sqrt(Math.Pow(x-.5,2)+Math.Pow(y-.5,2))/(.15+.65*strength);
            return strength*(r<=.55?.75+(.16-.75)*r/.55:r<1?.16*(1-r)/.45:0);
        }
        (RenderTargetBitmap Bitmap,double Error) Capture(string name,double strength)
        {
            var width=(int)Math.Ceiling(glow.ActualWidth*2);var height=(int)Math.Ceiling(glow.ActualHeight*2);
            var bitmap=new RenderTargetBitmap(width,height,192,192,PixelFormats.Pbgra32);bitmap.Render(glow);
            var bytes=new byte[width*height*4];bitmap.CopyPixels(bytes,width*4,0);
            var errors=new List<double>();
            foreach(var x in new[]{.08,.18,.34,.5,.66,.82,.92})foreach(var y in new[]{.35,.5,.65})
            {
                var px=RuntimeCompat.Clamp((int)(x*width),0,width-1);var py=RuntimeCompat.Clamp((int)(y*height),0,height-1);
                var actual=bytes[(py*width+px)*4+3]/255d;
                errors.Add(Math.Abs(actual-Expected((px+.5)/width,(py+.5)/height,strength)));
            }
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using(var file=File.Create(Path.Combine(output,name+".png")))encoder.Save(file);
            var scene=new RenderTargetBitmap(width,height,192,192,PixelFormats.Pbgra32);
            scene.Render(Field<FrameworkElement>("OuterBorder"));
            var sceneEncoder=new PngBitmapEncoder();sceneEncoder.Frames.Add(BitmapFrame.Create(scene));
            using(var sceneFile=File.Create(Path.Combine(output,name+"-scene.png")))sceneEncoder.Save(sceneFile);
            samples.Add(new{name,strength,width,height,maxError=errors.Max()});return(bitmap,errors.Max());
        }
        check("reminder remains hit-test transparent",!glow.IsHitTestVisible,null);
        var contentZ=Math.Max(Panel.GetZIndex(Field<FrameworkElement>("CompactPanel")),Panel.GetZIndex(Field<FrameworkElement>("DetailsPanel")));
        check("Mac reminder overlays content below the outer outline",Panel.GetZIndex(glow)>contentZ&&Panel.GetZIndex(glow)<Panel.GetZIndex(Field<FrameworkElement>("Outline")),new{glowZ=Panel.GetZIndex(glow),contentZ});
        foreach(var strength in new[]{0d,.25,.5,.75,1})
        {
            glow.Opacity=strength;await Task.Delay(45);
            var captured=Capture("strength-"+strength.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture),strength);
            check("actual glow pixels match Mac radius and stops at strength "+strength,captured.Error<.025,new{strength,captured.Error});
        }
        glow.Opacity=0;
        var picker=Field<ComboBox>("ReminderPicker");
        void Select(bool value)=>picker.SelectedItem=picker.Items.Cast<ComboBoxItem>().Single(i=>i.Tag?.ToString()==(value?"true":"false"));
        Select(false);var watch=Stopwatch.StartNew();Select(true);
        check("reminder requests Mac 30 fps independently of duration",Field<AnimationClock?>("reminderClock") is {} requested&&Timeline.GetDesiredFrameRate(requested.Timeline)==30,null);
        var curve=new List<(double Seconds,double Strength)>();var frameErrors=new List<double>();var frame=0;
        while(watch.Elapsed.TotalSeconds<1.8)
        {
            var time=watch.Elapsed.TotalSeconds;var strength=glow.Opacity;curve.Add((time,strength));
            if(time>=.15+frame*.2&&frame<7){frameErrors.Add(Capture("timeline-"+frame.ToString("00"),strength).Error);frame++;}
            await Task.Delay(20);
        }
        var peak=curve.MaxBy(p=>p.Strength);
        var maxTimingError=curve.Where(p=>p.Seconds<1.5).Max(p=>Math.Abs(p.Strength-(1-Math.Cos(p.Seconds/1.5*Math.PI*2))/2));
        check("actual animation clock retains 1.5 second cosine preview",peak.Strength>.95&&peak.Seconds is >.6 and <.9&&maxTimingError<.2,new{peak.Seconds,peak.Strength,maxTimingError,samples=curve.Count});
        var changedSamples=curve.Where(p=>p.Seconds is >.1 and <1.4).Select(p=>p.Strength).Distinct().Count();
        check("actual sampled reminder updates are bounded near 30 fps",changedSamples is >=27 and <=43,new{changedSamples,observedSeconds=1.3,scope="Unique observed strengths sampled near 50Hz; bounds permit scheduling delay, not compositor FPS."});
        check("actual animated middle frames expand with their sampled strength",frameErrors.Count==7&&frameErrors.Max()<.03,new{frames=frameErrors.Count,maxError=frameErrors.Max()});
        check("preview completion leaves no animated glow or clock",!glow.HasAnimatedProperties&&glow.Opacity==0&&Field<AnimationClock?>("reminderClock")==null,null);
        Select(false);Select(true);await Task.Delay(120);Select(false);await Task.Delay(100);
        check("quick cancellation clears pixels and clock",glow.Opacity==0&&!glow.HasAnimatedProperties&&Field<AnimationClock?>("reminderClock")==null&&Capture("cancelled",0).Error==0,null);
        settings.ReduceMotion=true;Select(true);await Task.Delay(80);
        check("reduced motion does not render expanding flash",glow.Opacity==0&&!glow.HasAnimatedProperties&&Field<AnimationClock?>("reminderClock")==null,null);
        File.WriteAllText(Path.Combine(output,"geometry-samples.json"),JsonSerializer.Serialize(new{samples,curve=curve.Select(p=>new{p.Seconds,p.Strength}),scope="Actual product WPF element and clock, 2x RenderTargetBitmap exports. Settings/timestamps controlled; not desktop compositor frames, Mac runtime, physical input or game acceptance."},AtomicStore.JsonOptions));
    }
}
