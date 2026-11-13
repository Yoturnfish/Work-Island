using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private readonly record struct GlyphFootprint(int Left,int Top,int Width,int Height,double X,double Y,long Alpha);
    private static GlyphFootprint Footprint(Bitmap image)
    {
        var left=image.Width;var top=image.Height;var right=-1;var bottom=-1;long alpha=0;double xsum=0,ysum=0;
        for(var y=0;y<image.Height;y++)for(var x=0;x<image.Width;x++)
        {
            var a=image.GetPixel(x,y).A;alpha+=a;xsum+=(x+.5)*a;ysum+=(y+.5)*a;
            if(a>180){left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y);}
        }
        return new(left,top,right-left+1,bottom-top+1,xsum/alpha,ysum/alpha,alpha);
    }
    private static void RunStatusAlignmentChecks(string output,Action<string,bool,object?> check)
    {
        using var artwork=new StatusGlyphArtwork();var metrics=new List<object>();
        foreach(var size in new[]{16,20,24,32,64})
        {
            using var clock=artwork.Render("tracking","green",.5,size);
            using var coffee=artwork.Render("away","green",.5,size);
            using var pulse=artwork.Render("away","green",.5,size,coffeePulse:1);
            var a=Footprint(clock);var b=Footprint(coffee);var p=Footprint(pulse);
            var endpointAligned=Math.Abs(a.X-b.X)<.35&&Math.Abs(a.Y-b.Y)<.35&&Math.Abs(a.Left-b.Left)<=1&&Math.Abs(a.Top-b.Top)<=1&&Math.Abs(a.Width-b.Width)<=1&&Math.Abs(a.Height-b.Height)<=1;
            check($"{size}px clock and coffee retain the same center and visible diameter",endpointAligned,new{clock=a,coffee=b});
            check($"{size}px coffee breathing does not resize or clip the shared icon footprint",b.Left==p.Left&&b.Top==p.Top&&b.Width==p.Width&&b.Height==p.Height&&Math.Abs(b.X-p.X)<.1&&Math.Abs(b.Y-p.Y)<.1,new{coffee=b,pulse=p});
            var motion=new StatusItemMotion();motion.Retarget(Color.FromArgb(64,255,97),"tracking",.5,false,0,null);motion.Retarget(Color.FromArgb(255,149,0),"away",.5,false,1,0);
            var frames=new List<object>();double minimumRatio=1,maximumDrift=0;
            for(var i=0;i<=18;i++)
            {
                var now=1+i*.35/18;var blend=motion.GlyphAt(now);
                using var frame=artwork.Render("away","green",.5,size,motion.ColorAt(now),scanPhase:motion.ScanPhase(now),blend:blend);
                var f=Footprint(frame);var expected=a.Alpha*blend.Clock+b.Alpha*blend.Coffee;var ratio=f.Alpha/expected;
                minimumRatio=Math.Min(minimumRatio,ratio);maximumDrift=Math.Max(maximumDrift,Math.Max(Math.Abs(f.X-a.X),Math.Abs(f.Y-a.Y)));
                var file=$"transition-{size}-{i:00}.png";frame.Save(Path.Combine(output,file));frames.Add(new{file,seconds=i*.35/18,blend,footprint=f,opacityRatio=ratio});
            }
            check($"{size}px native clock to coffee crossfade keeps opacity and position throughout 350ms",minimumRatio>.98&&maximumDrift<.4,new{minimumRatio,maximumDrift});
            metrics.Add(new{size,clock=a,coffee=b,pulse=p,minimumRatio,maximumDrift,frames});
        }
        File.WriteAllText(Path.Combine(output,"alignment-metrics.json"),JsonSerializer.Serialize(metrics,AtomicStore.JsonOptions));
    }
}
