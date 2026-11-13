using System.IO;
using Drawing=System.Drawing;

namespace WorkIsland.App;

public static class TrayArtwork
{
    public static Drawing.Bitmap RenderTime(double seconds,string style,string state,string accent,double progress,int size,Drawing.Color? presentationColor=null,bool transparent=false,double[]? cellOpacities=null)
    {
        using var renderer=new StatusTimeArtwork();return renderer.Render(seconds,style,state,accent,progress,size,presentationColor,transparent,cellOpacities);
    }

    internal static Drawing.Color Accent(string accent)=>accent switch
    {"blue"=>Drawing.Color.FromArgb(51,140,255),"yellow"=>Drawing.Color.FromArgb(255,214,10),"cyan"=>Drawing.Color.FromArgb(50,230,230),"pink"=>Drawing.Color.FromArgb(255,55,95),"orange"=>Drawing.Color.FromArgb(255,153,194),"purple"=>Drawing.Color.FromArgb(191,90,242),_=>Drawing.Color.FromArgb(64,255,97)};
    public static Drawing.Bitmap Render(string state,string accent,int size)
    {
        var bitmap=new Drawing.Bitmap(size,size,Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics=Drawing.Graphics.FromImage(bitmap);
        graphics.Clear(Drawing.Color.Transparent);graphics.SmoothingMode=Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode=Drawing.Drawing2D.PixelOffsetMode.HighQuality;graphics.ScaleTransform(size/32f,size/32f);
        var color=state=="paused"?Drawing.Color.FromArgb(173,173,173):state=="away"?Drawing.Color.FromArgb(255,149,0):Accent(accent);
        using var background=new Drawing.SolidBrush(color);using var black=new Drawing.SolidBrush(Drawing.Color.Black);
        using var line=new Drawing.Pen(Drawing.Color.Black,1.8f){StartCap=Drawing.Drawing2D.LineCap.Round,EndCap=Drawing.Drawing2D.LineCap.Round};
        graphics.FillEllipse(background,1,1,30,30);
        if(state=="paused"){graphics.FillRectangle(black,12,10,3,12);graphics.FillRectangle(black,17,10,3,12);}
        else if(state=="away")
        {
            graphics.FillRectangle(black,10,13,11,5);graphics.FillEllipse(black,10,15,11,5);
            graphics.DrawArc(line,18,12,6,6,270,180);graphics.DrawLine(line,9,22,23,22);
        }
        else
        {
            graphics.FillEllipse(black,9,9,14,14);
            using var hand=new Drawing.Pen(color,2){StartCap=Drawing.Drawing2D.LineCap.Round,EndCap=Drawing.Drawing2D.LineCap.Round};
            graphics.DrawLines(hand,[new(12,16),new(16,16),new(16,11)]);
        }
        return bitmap;
    }
    public static byte[] Ico(string state,string accent)
    {
        int[] sizes=[16,20,24,32,48,64,256];var images=new List<byte[]>();
        foreach(var size in sizes){using var bitmap=Render(state,accent,size);using var data=new MemoryStream();bitmap.Save(data,Drawing.Imaging.ImageFormat.Png);images.Add(data.ToArray());}
        using var output=new MemoryStream();using var writer=new BinaryWriter(output);
        writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);
        var offset=6+16*sizes.Length;
        for(var i=0;i<sizes.Length;i++)
        {
            writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)0);writer.Write((byte)0);
            writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(images[i].Length);writer.Write(offset);offset+=images[i].Length;
        }
        foreach(var bytes in images)writer.Write(bytes);return output.ToArray();
    }
    public static Drawing.Icon Icon(string state,string accent)
    {
        using var stream=new MemoryStream(Ico(state,accent));using var original=new Drawing.Icon(stream,new Drawing.Size(32,32));return (Drawing.Icon)original.Clone();
    }
}
