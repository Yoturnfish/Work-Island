using System.Drawing;
using System.Text.Json;

// Offline analysis of owned screen pixels; no windows, input or process control.
internal static class Program
{
    private static int Main(string[] args)
    {
        if(args.Length!=2){Console.Error.WriteLine("PulseEvidence <probe-directory> <new-output-directory>");return 2;}
        var root=Path.GetFullPath(args[0]);var output=Path.GetFullPath(args[1]);if(Directory.Exists(output))throw new IOException("Keep existing evidence: "+output);Directory.CreateDirectory(output);
        using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"native-pulse-probe.json")));var report=document.RootElement;var rows=report.GetProperty("rows").EnumerateArray().ToArray();var frames=report.GetProperty("motionRows").EnumerateArray().ToArray();
        var dpi=report.GetProperty("dpi").GetProperty("dpiScaleX").GetDouble();var margin=(int)Math.Ceiling(23*dpi);var checks=new List<object>();var measurements=new List<object>();
        void Check(string name,bool passed,object evidence)=>checks.Add(new{name,passed,evidence});
        foreach(var state in new[]{"away","paused"})foreach(var style in new[]{"matrix","waveform"})
        {
            bool Matches(JsonElement row)=>row.GetProperty("state").GetString()==state&&row.GetProperty("style").GetString()==style;
            var group=frames.Where(Matches).ToArray();if(group.Length==0)continue;var stem=state+"-"+style;
            var zeroRow=rows.Single(row=>Matches(row)&&row.GetProperty("alpha").GetDouble()==.5&&row.GetProperty("phase").GetDouble()==0);
            var peakRow=rows.Single(row=>Matches(row)&&row.GetProperty("alpha").GetDouble()==.5&&row.GetProperty("phase").GetDouble()==.5);
            using var zero=new Bitmap(zeroRow.GetProperty("nativePath").GetString()!);using var peak=new Bitmap(peakRow.GetProperty("nativePath").GetString()!);
            var cx=margin+zeroRow.GetProperty("center").GetProperty("x").GetDouble()*dpi;var cy=margin+zeroRow.GetProperty("center").GetProperty("y").GetDouble()*dpi;
            var glyphPoints=new List<Point>();for(var y=(int)cy-25;y<cy+25;y++)for(var x=(int)cx-25;x<cx+25;x++)glyphPoints.Add(new(x,y));
            using var cells=new Bitmap(Path.Combine(root,stem+"-cells.png"));var cellPoints=new List<Point>();for(var y=0;y<cells.Height;y++)for(var x=0;x<cells.Width;x++)if(cells.GetPixel(x,y).A>200)cellPoints.Add(new(x+margin,y+margin));
            var samples=new List<object>();var times=new List<double>();var glyphValues=new List<double>();var cellsValues=new List<double>();
            var backgroundPoint=new Point(margin+cells.Width/2,margin+cells.Height-5);var backgroundReference=zero.GetPixel(backgroundPoint.X,backgroundPoint.Y);var maxBackgroundDifference=0;
            double Projection(Bitmap current,List<Point> points)
            {
                double sum=0,norm=0;foreach(var point in points)
                {
                    var a=zero.GetPixel(point.X,point.Y);var b=peak.GetPixel(point.X,point.Y);var c=current.GetPixel(point.X,point.Y);
                    var dr=b.R-a.R;var dg=b.G-a.G;var db=b.B-a.B;norm+=dr*dr+dg*dg+db*db;sum+=(c.R-a.R)*dr+(c.G-a.G)*dg+(c.B-a.B)*db;
                }
                return norm>0?sum/norm:double.NaN;
            }
            foreach(var frame in group)
            {
                var t=frame.GetProperty("elapsedSeconds").GetDouble();var path=Path.GetFullPath(Path.Combine(root,frame.GetProperty("file").GetString()!));if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Frame escaped source root");
                using var current=new Bitmap(path);var glyph=Projection(current,glyphPoints);var cell=state=="away"?Projection(current,cellPoints):double.NaN;
                var background=current.GetPixel(backgroundPoint.X,backgroundPoint.Y);var backgroundDifference=Math.Max(Math.Abs(background.R-backgroundReference.R),Math.Max(Math.Abs(background.G-backgroundReference.G),Math.Abs(background.B-backgroundReference.B)));maxBackgroundDifference=Math.Max(maxBackgroundDifference,backgroundDifference);
                times.Add(t);glyphValues.Add(glyph);cellsValues.Add(cell);samples.Add(new{elapsedSeconds=t,glyphProjection=glyph,cellsProjection=double.IsNaN(cell)?(double?)null:cell,backgroundDifference,file=path});
            }
            var glyphOrigin=0d;var cellsOrigin=0d;
            if(report.TryGetProperty("timingOrigins",out var origins))
            {
                var origin=origins.EnumerateArray().Single(Matches);glyphOrigin=origin.GetProperty("glyphElapsedSecondsAtStart").GetDouble();cellsOrigin=origin.GetProperty("cellsElapsedSecondsAtStart").GetDouble();
            }
            double Correlation(double[] actual,double period,double origin)
            {
                var expected=times.Select(t=>.5-.5*Math.Cos(2*Math.PI*(t+origin)/period)).ToArray();var ma=actual.Average();var mb=expected.Average();double ab=0,aa=0,bb=0;
                for(var i=0;i<actual.Length;i++){var a=actual[i]-ma;var b=expected[i]-mb;ab+=a*b;aa+=a*a;bb+=b*b;}return aa*bb>0?ab/Math.Sqrt(aa*bb):0;
            }
            var glyphPeriod=state=="paused"?1.8:5.6;var glyphCorrelation=Correlation(glyphValues.ToArray(),glyphPeriod,glyphOrigin);var gaps=times.Zip(times.Skip(1),(a,b)=>b-a).ToArray();
            Check(stem+" live glyph pixels follow the expected full cycle",glyphCorrelation>.9&&glyphValues.Min()<.15&&glyphValues.Max()>.85,new{glyphPeriod,glyphCorrelation,min=glyphValues.Min(),max=glyphValues.Max(),samples=times.Count});
            if(state=="away")
            {
                // The fixed peak reference uses phase .5 for BOTH icon and cells.
                // Cells use their own 2.8-second timeline in live capture.
                var cellCorrelation=Correlation(cellsValues.ToArray(),2.8,cellsOrigin);Check(stem+" live unfilled pixels follow the separate 2.8-second cycle",cellCorrelation>.95&&cellsValues.Min()<.1&&cellsValues.Max()>.9,new{cellCorrelation,min=cellsValues.Min(),max=cellsValues.Max()});
            }
            Check(stem+" screen capture covers cycles without large gaps",times[^1]>glyphPeriod-.15&&gaps.Max()<.25,new{first=times[0],last=times[^1],maxGap=gaps.Max(),meanGap=gaps.Average(),scope="Capture cadence only; does not measure compositor refresh rate."});
            Check(stem+" material background stays at the expected alpha including handoff frames",maxBackgroundDifference<=3,new{maxBackgroundDifference,point=new{x=backgroundPoint.X,y=backgroundPoint.Y},scope="Owned solid-white background, stable state and fixed logical alpha only. Does not validate all navigation or material transitions."});
            var movie=Path.Combine(output,stem);Directory.CreateDirectory(Path.Combine(movie,"frames"));
            foreach(var frame in group){var path=Path.Combine(root,frame.GetProperty("file").GetString()!);File.Copy(path,Path.Combine(movie,"frames",Path.GetFileName(path)));}
            File.WriteAllText(Path.Combine(movie,"motion-capture.json"),JsonSerializer.Serialize(new{durationSeconds=times[^1]+.02,frameCount=group.Length,frames=group.Select(frame=>new{file=Path.GetFileName(frame.GetProperty("file").GetString()!),elapsedSeconds=frame.GetProperty("elapsedSeconds").GetDouble()}),scope="Original real screen-frame timestamps retained; last encoded frame held 20ms because capture-stop time was not recorded. No resizing or slow motion."},new JsonSerializerOptions{WriteIndented=true}));
            measurements.Add(new{state,style,glyphPeriod,glyphCorrelation,glyphOrigin,cellsOrigin,samples});
        }
        var passed=checks.Count>0&&checks.All(check=>JsonSerializer.SerializeToElement(check).GetProperty("passed").GetBoolean());
        File.WriteAllText(Path.Combine(output,"pulse-pixel-timing.json"),JsonSerializer.Serialize(new{passed,checks,measurements,scope="Offline projections of actual desktop pixels onto independently captured native fixed endpoint images. Timed scalar getters are not used. Verifies visible cyclic changes, not input, renderer handoff, frame-perfect Mac parity or performance."},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(new{passed,checks=checks.Count,output}));return passed?0:1;
    }
}
