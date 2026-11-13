using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Media=System.Windows.Media;

namespace WorkIsland.App;

// The Shell still owns one icon slot. Only its small raster changes; there is
// no extra window, font shaping, timer or Explorer modification here.
public sealed class StatusGlyphArtwork:IDisposable
{
    private readonly SolidBrush[] sheen=Enumerable.Range(0,9).Select(i=>new SolidBrush(Color.FromArgb((int)Math.Round(i/8d*30),255,255,255))).ToArray();
    private readonly SolidBrush dimFace=new(Color.Transparent),clockFace=new(Color.Transparent),orb=new(Color.Orange),black=new(Color.Black),white=new(Color.FromArgb(235,255,255,255));
    private readonly Pen clockHands=new(Color.Transparent){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round};
    private readonly GraphicsPath cup=CreateCup();
    private Bitmap? layer;
    private int[]? layerPixels,blendChannels;
    private GraphicsPath? clockClip;
    private int clockClipSize;
    private Color palette;
    private bool disposed;

    public static int ProgressKey(double progress)=>(int)Math.Round(Normalize(progress)*1000);
    private static double Normalize(double value)=>double.IsNaN(value)?0:Math.Clamp(value,0,1);
    public Bitmap Render(string state,string accent,double progress,int size,Color? presentationColor=null,bool transparent=false,
        double scanPhase=0,StatusGlyphBlend? blend=null,double coffeePulse=0)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(size<8||size>256)throw new ArgumentOutOfRangeException(nameof(size));
        var color=presentationColor??(state=="away"?Color.FromArgb(255,149,0):state=="paused"?Color.FromArgb(173,173,173):TrayArtwork.Accent(accent));
        if(palette!=color)
        {
            palette=color;
            clockFace.Color=clockHands.Color=color;
            dimFace.Color=Color.FromArgb(color.A,(int)(color.R*.58),(int)(color.G*.58),(int)(color.B*.58));
        }
        var weights=blend??(state=="tracking"?new StatusGlyphBlend(1,0,0):state=="paused"?new(0,0,1):new StatusGlyphBlend(0,1,0));
        var bitmap=new Bitmap(size,size,PixelFormat.Format32bppArgb);
        if(weights is {Clock:1,Coffee:0,Pause:0} or {Clock:0,Coffee:1,Pause:0} or {Clock:0,Coffee:0,Pause:1})
        {
            using var graphics=Graphics.FromImage(bitmap);graphics.Clear(Color.Transparent);
            if(weights.Clock==1)DrawClock(graphics,progress,size,scanPhase);
            else if(weights.Coffee==1)DrawCoffee(graphics,color,size,coffeePulse);
            else DrawPause(graphics,size,transparent);
            return bitmap;
        }
        if(layer is null||layer.Width!=size)
        {
            layer?.Dispose();layer=new Bitmap(size,size,PixelFormat.Format32bppPArgb);
            layerPixels=new int[size*size];blendChannels=new int[size*size*4];
        }
        var pixels=layerPixels!;var channels=blendChannels!;Array.Clear(channels);
        var bounds=new Rectangle(0,0,size,size);
        void DrawLayer(double alpha,Action<Graphics> draw)
        {
            if(alpha<=0)return;
            using(var scratch=Graphics.FromImage(layer)){scratch.Clear(Color.Transparent);draw(scratch);}
            var data=layer.LockBits(bounds,ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
            try{Marshal.Copy(data.Scan0,pixels,0,pixels.Length);}finally{layer.UnlockBits(data);}
            var weight=(int)Math.Round(Math.Clamp(alpha,0,1)*65535);
            for(var i=0;i<pixels.Length;i++)
            {
                var value=(uint)pixels[i];var index=i*4;
                channels[index]+=(int)(value&255)*weight;channels[index+1]+=(int)((value>>8)&255)*weight;
                channels[index+2]+=(int)((value>>16)&255)*weight;channels[index+3]+=(int)(value>>24)*weight;
            }
        }
        DrawLayer(weights.Clock,g=>DrawClock(g,progress,size,scanPhase));
        DrawLayer(weights.Coffee,g=>DrawCoffee(g,color,size,coffeePulse));
        DrawLayer(weights.Pause,g=>DrawPause(g,size,transparent));
        // Interpolate premultiplied pixels instead of drawing two fading whole
        // icons over one another. Their shared orb must not dip to 75% opacity.
        for(var i=0;i<pixels.Length;i++)
        {
            var index=i*4;var a=Math.Clamp((channels[index+3]+32767)/65535,0,255);
            int Straight(int offset)=>a==0?0:Math.Clamp((((channels[index+offset]+32767)/65535)*255+a/2)/a,0,255);
            pixels[i]=(a<<24)|(Straight(2)<<16)|(Straight(1)<<8)|Straight(0);
        }
        var target=bitmap.LockBits(bounds,ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
        try{Marshal.Copy(pixels,0,target.Scan0,pixels.Length);}finally{bitmap.UnlockBits(target);}
        return bitmap;
    }
    private void DrawClock(Graphics g,double progress,int size,double phase)
    {
        g.SmoothingMode=SmoothingMode.AntiAlias;
        var center=size/2f;var inset=size/32f;var diameter=size-2*inset;
        if(clockClip is null||clockClipSize!=size)
        {
            clockClip?.Dispose();clockClip=new GraphicsPath();clockClipSize=size;
            clockClip.AddEllipse(inset,inset,diameter,diameter);
        }
        var saved=g.Save();g.SetClip(clockClip,CombineMode.Intersect);
        g.FillEllipse(dimFace,inset,inset,diameter,diameter);
        var progressValue=ProgressKey(progress)/1000f;
        if(progressValue>=1)g.FillEllipse(clockFace,inset,inset,diameter,diameter);
        else if(progressValue>0)g.FillPie(clockFace,inset,inset,diameter,diameter,-90,360*progressValue);
        var phaseValue=(double.IsFinite(phase)?phase:0)%1;if(phaseValue<0)phaseValue++;
        // A subtle rotating sheen stays inside the same ordinary clock face.
        // No dot matrix, detached rim, or extra progress ring is drawn.
        for(var i=0;i<48;i++)
        {
            var behind=(phaseValue-(i+.5)/48+1)%1;var strength=Math.Max(0,1-behind/.28);
            var light=(int)Math.Round(strength*strength*8);if(light==0)continue;
            g.FillPie(sheen[light],inset,inset,diameter,diameter,-90+i*7.5f,7.5f);
        }
        g.Restore(saved);
        // Match IslandWindow.xaml ClockIcon: a black 15.2-DIP face inside
        // the 34-DIP status orb, with connected, round-capped clock hands.
        var scale=size/34f;var offset=center-7.6f*scale;
        g.FillEllipse(black,offset,offset,15.2f*scale,15.2f*scale);
        clockHands.Width=Math.Max(1,1.5f*scale);
        g.DrawLines(clockHands,new PointF[]{new(offset+7.6f*scale,offset+3.3f*scale),new(center,center),new(offset+4.1f*scale,center)});
    }
    private void DrawCoffee(Graphics g,Color color,int size,double pulse)
    {
        g.SmoothingMode=SmoothingMode.AntiAlias;
        var saved=g.Save();g.ScaleTransform(size/32f,size/32f);
        orb.Color=color;g.FillEllipse(orb,1,1,30,30);
        var scale=1+(float)Math.Clamp(pulse,0,1)*.08f;
        g.TranslateTransform(16,16);g.ScaleTransform(scale,scale);g.TranslateTransform(-16,-16);
        // Same outline, optical offset and proportions as the main 34-DIP orb.
        g.ScaleTransform(32f/34,32f/34);g.TranslateTransform((34-16.4f)/2-.5f,(34-16f)/2-.5f);g.ScaleTransform(16.4f/20,16f/20);
        g.FillPath(black,cup);g.Restore(saved);
    }
    private void DrawPause(Graphics g,int size,bool transparent)
    {
        g.SmoothingMode=SmoothingMode.None;
        if(!transparent){using var background=new SolidBrush(Color.FromArgb(242,15,18,15));using var shape=Rounded(size);g.FillPath(background,shape);}
        var bar=Math.Max(2,(int)Math.Round(size*.16));var height=(int)Math.Round(size*.55);var top=(size-height)/2;
        g.FillRectangle(black,(int)(size*.27)-1,top-1,bar+2,height+2);g.FillRectangle(black,(int)(size*.57)-1,top-1,bar+2,height+2);
        g.FillRectangle(white,(int)(size*.27),top,bar,height);g.FillRectangle(white,(int)(size*.57),top,bar,height);
    }
    private static GraphicsPath Rounded(int size)
    {
        var p=new GraphicsPath();var r=Math.Max(2,size/4f);p.AddArc(0,0,r,r,180,90);p.AddArc(size-r,0,r,r,270,90);p.AddArc(size-r,size-r,r,r,0,90);p.AddArc(0,size-r,r,r,90,90);p.CloseFigure();return p;
    }
    private static GraphicsPath CreateCup()
    {
        var result=new GraphicsPath(FillMode.Alternate);
        var geometry=Media.Geometry.Parse(MacSymbol.CupPath).GetFlattenedPathGeometry(.01,Media.ToleranceType.Absolute);
        foreach(var figure in geometry.Figures)
        {
            var points=new List<PointF>{new((float)figure.StartPoint.X,(float)figure.StartPoint.Y)};
            foreach(var segment in figure.Segments)
            {
                if(segment is Media.PolyLineSegment poly)points.AddRange(poly.Points.Select(p=>new PointF((float)p.X,(float)p.Y)));
                else if(segment is Media.LineSegment line)points.Add(new((float)line.Point.X,(float)line.Point.Y));
            }
            if(points.Count<2)continue;result.StartFigure();result.AddLines(points.ToArray());if(figure.IsClosed)result.CloseFigure();
        }
        return result;
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;layer?.Dispose();clockClip?.Dispose();cup.Dispose();layerPixels=blendChannels=null;
        foreach(var brush in sheen)brush.Dispose();dimFace.Dispose();clockFace.Dispose();clockHands.Dispose();orb.Dispose();black.Dispose();white.Dispose();
    }
}
