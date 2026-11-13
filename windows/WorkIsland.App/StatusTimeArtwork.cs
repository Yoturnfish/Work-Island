using System.Drawing;
using System.Drawing.Imaging;

namespace WorkIsland.App;

// Font layout is expensive in GDI+. Cache the color-independent text mask and
// tint it at composition time, so scanning never re-creates fonts or glyphs.
public sealed class StatusTimeArtwork : IDisposable
{
    private Bitmap? text;
    private string textKey="";
    private readonly ImageAttributes tint=new();
    private Color tintColor;
    public Bitmap Render(double seconds,string style,string state,string accent,double progress,int size,Color? presentationColor=null,bool transparent=false,double[]? cellOpacities=null)
    {
        var value=(long)Math.Clamp(double.IsFinite(seconds)?seconds:0,0,315360000);
        var upper=style=="minutesSeconds"?$"{value/60}M":$"{value/3600}H";
        var lower=style=="minutesSeconds"?$"{value%60:00}S":$"{value/60%60:00}M";
        var key=upper+"/"+lower;
        if(text is null || key!=textKey)
        {
            text?.Dispose();text=new Bitmap(32,32,PixelFormat.Format32bppArgb);textKey=key;
            using var glyphs=Graphics.FromImage(text);glyphs.Clear(Color.Transparent);glyphs.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap};
            void Line(string content,float top)
            {
                using var font=new Font("Segoe UI",content.Length>4?9:content.Length>3?10:content.Length>2?12:14,FontStyle.Bold,GraphicsUnit.Pixel);
                glyphs.DrawString(content,font,Brushes.White,new RectangleF(0,top,32,14),format);
            }
            Line(upper,-1);Line(lower,12);
        }
        var color=presentationColor??(state=="paused"?Color.FromArgb(173,173,173):state=="away"?Color.FromArgb(255,149,0):TrayArtwork.Accent(accent));
        if(color!=tintColor)
        {
            tintColor=color;tint.SetColorMatrix(new ColorMatrix{Matrix00=color.R/255f,Matrix11=color.G/255f,Matrix22=color.B/255f,Matrix33=color.A/255f});
        }
        var bitmap=new Bitmap(size,size,PixelFormat.Format32bppArgb);
        using var graphics=Graphics.FromImage(bitmap);graphics.Clear(Color.Transparent);graphics.ScaleTransform(size/32f,size/32f);
        if(!transparent)
        {
            using var background=new SolidBrush(Color.FromArgb(242,15,18,15));using var shape=new System.Drawing.Drawing2D.GraphicsPath();
            shape.AddArc(0,0,8,8,180,90);shape.AddArc(24,0,8,8,270,90);shape.AddArc(24,24,8,8,0,90);shape.AddArc(0,24,8,8,90,90);shape.CloseFigure();graphics.FillPath(background,shape);
        }
        graphics.DrawImage(text,new Rectangle(0,0,32,32),0,0,32,32,GraphicsUnit.Pixel,tint);
        var lit=(int)Math.Clamp(Math.Floor(Math.Clamp(progress,0,1)*10),0,10);
        for(var i=0;i<10;i++)
        {
            var alpha=cellOpacities is {Length:10}?cellOpacities[i]:i<lit?.72:.10;
            using var cell=new SolidBrush(Color.FromArgb((int)Math.Round(Math.Clamp(alpha,0,1)*255),color));
            graphics.FillRectangle(cell,2+i*2.8f,28,1.8f,2);
        }
        return bitmap;
    }
    public void Dispose(){text?.Dispose();text=null;tint.Dispose();}
}
