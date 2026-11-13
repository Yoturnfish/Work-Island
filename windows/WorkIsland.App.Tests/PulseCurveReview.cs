using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media.Animation;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunPulseCurveReview(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        var engine=(TrackingEngine)typeof(IslandWindow).GetField("engine",Flags)!.GetValue(window)!;
        engine.Data.Settings.ReduceMotion=true;typeof(IslandWindow).GetMethod("Render",Flags|BindingFlags.DeclaredOnly)!.Invoke(window,null);
        var rows=new List<object>();
        foreach(var (name,from,to,leg) in new[]{("paused-icon",1d,.86,.9),("away-icon",1d,1.08,2.8),("away-matrix",.10,.35,1.4),("away-waveform",.18,.35,1.4)})
        {
            var timeline=new DoubleAnimation(from,to,TimeSpan.FromSeconds(leg)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}};
            var clock=(AnimationClock)timeline.CreateClock(true);clock.Controller!.Begin();await Task.Delay(30);
            double Sample(double phase)
            {
                clock.Controller.SeekAlignedToLastTick(TimeSpan.FromTicks((long)Math.Round(phase*leg*2*TimeSpan.TicksPerSecond)),TimeSeekOrigin.BeginTime);
                return (double)timeline.GetCurrentValue(from,from,clock);
            }
            try
            {
                const int intervals=128;var values=Enumerable.Range(0,intervals+1).Select(index=>(float)Sample(index/(double)intervals)).ToArray();
                var maxError=0d;var meanError=0d;
                for(var index=0;index<=1024;index++)
                {
                    var position=index/1024d*intervals;var low=Math.Min(intervals-1,(int)position);var amount=position-low;
                    var interpolated=values[low]+(values[low+1]-values[low])*amount;var error=Math.Abs(interpolated-Sample(index/1024d));
                    maxError=Math.Max(maxError,error);meanError+=error/1025;
                }
                check(name+" sample table covers actual WPF endpoints and reverse",Math.Abs(values[0]-from)<.000001&&Math.Abs(values[64]-to)<.000001&&Math.Abs(values[128]-from)<.000001&&values.Distinct().Count()>16,null);
                check(name+" linear keyframe interpolation matches actual WPF clock",maxError<.0001,new{maxError,meanError});
                rows.Add(new{name,from,to,singleLegSeconds=leg,fullCycleSeconds=leg*2,intervals,maxError,meanError,keyframes=values.Select((value,index)=>new{progress=index/(double)intervals,value}).ToArray()});
            }
            finally{clock.Controller.Remove();}
        }
        File.WriteAllText(Path.Combine(output,"pulse-curves.json"),JsonSerializer.Serialize(new{rows,scope="Samples actual owned WPF DoubleAnimation clocks, then independently checks dense WPF samples against 128-segment float linear interpolation. Prospective compositor keyframe data only; no native animation, screen pixels, handoff, resource saving or product changes proved."},AtomicStore.JsonOptions));
    }
}
