using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class Program
{
    [STAThread]private static int Main(string[] args)
    {
        if(args.Length!=1)return 2;
        var root=Path.GetFullPath(args[0]);var output=Path.Combine(root,"sheets");if(Directory.Exists(output))throw new IOException("Preserve existing sheets");Directory.CreateDirectory(output);
        using var source=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"motion-capture.json")));
        var frames=source.RootElement.GetProperty("frames").EnumerateArray().ToArray();var events=source.RootElement.GetProperty("events").EnumerateArray().ToArray();
        double At(string name)=>events.First(e=>e.GetProperty("name").GetString()==name).GetProperty("at").GetDouble();
        var inputs=new List<object>();var sheets=new List<string>();
        JsonElement Nearest(double t)=>frames.MinBy(f=>Math.Abs(f.GetProperty("elapsedSeconds").GetDouble()-t));
        BitmapFrame Load(JsonElement f)=>BitmapFrame.Create(new Uri(Path.Combine(root,"frames",f.GetProperty("file").GetString()!)),BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
        var first=Load(frames[0]);var width=first.PixelWidth;var height=first.PixelHeight;var rowHeight=height+30;
        void Sheet(string name,(string Title,double[] Times)[] rows)
        {
            var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White,null,new Rect(0,0,width*4,rowHeight*rows.Length));
                for(var row=0;row<rows.Length;row++)for(var col=0;col<4;col++)
                {
                    var f=Nearest(rows[row].Times[col]);var at=f.GetProperty("elapsedSeconds").GetDouble();var file=f.GetProperty("file").GetString()!;
                    var label=$"{rows[row].Title} | {at:F3}s";
                    dc.DrawText(new FormattedText(label,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,Brushes.Black,1),new Point(col*width+4,row*rowHeight+4));
                    var bitmap=Load(f);dc.DrawImage(bitmap,new Rect(col*width,row*rowHeight+25,width,height));
                    inputs.Add(new{sheet=name,row,col,file,at,sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,"frames",file))))});
                }
            }
            var image=new RenderTargetBitmap(width*4,rowHeight*rows.Length,96,96,PixelFormats.Pbgra32);image.Render(visual);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));var path=Path.Combine(output,name+".png");using(var stream=File.Create(path))encoder.Save(stream);sheets.Add(path);
        }
        double[] Sequence(string name,params double[] offsets){var at=At(name);return offsets.Select(v=>v+at).ToArray();}
        Sheet("state-and-ink",[
            ("Tracking -> away",Sequence("away",0,.10,.23,.42)),
            ("Natural title adaptation",Sequence("natural-ink-start",0,.11,.24,.40)),
            ("Rapid state reversal",Sequence("ink-overlap-paused",0,.11,.24,.42)),
            ("Stable / handoff",Sequence("ink-overlap-tracking",0,.11,.24,.55))]);
        Sheet("navigation-and-reduction",[
            ("Root reverse",Sequence("expand",0,.06,.16,.42)),
            ("Settings page reverse",Sequence("settings",0,.06,.16,.42)),
            ("Reduce motion",Sequence("reduce-away",0,.08,.25,.40)),
            ("Resume motion",Sequence("resume-motion",0,.09,.27,.52))]);
        Sheet("native-pulse-cycles",[
            ("Paused after fade",Sequence("paused-full-cycle",.40,.85,1.30,2.0)),
            ("Away full cycle I",Sequence("away-full-cycle",.40,1.40,2.80,4.20)),
            ("Away full cycle II",Sequence("away-full-cycle",4.55,4.90,5.30,5.70)),
            ("Final tracking",Sequence("tracking-final",0,.12,.26,.60))]);
        var gaps=frames.Zip(frames.Skip(1),(a,b)=>b.GetProperty("elapsedSeconds").GetDouble()-a.GetProperty("elapsedSeconds").GetDouble()).Order().ToArray();
        File.WriteAllText(Path.Combine(output,"sheet-evidence.json"),JsonSerializer.Serialize(new{source=Path.Combine(root,"motion-capture.json"),sourceSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,"motion-capture.json")))) ,captureWidth=width,captureHeight=height,frameCount=frames.Length,durationSeconds=source.RootElement.GetProperty("durationSeconds").GetDouble(),medianGapSeconds=gaps[gaps.Length/2],maxGapSeconds=gaps.Max(),sheets,inputs,scope="Original captured pixels composed at 1:1 without retouching, nearest original timestamp to each requested sample. Labels outside image. Properties in source remain separate from physical pixels. Contact sheets are selected frames, not all-frame perceptual verification."},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"{frames.Length} frames; {sheets.Count} contact sheets.");return 0;
    }
}
