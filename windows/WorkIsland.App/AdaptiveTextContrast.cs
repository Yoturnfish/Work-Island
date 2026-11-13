using WorkIsland.Core;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Drawing = System.Drawing;
using Imaging = System.Drawing.Imaging;

namespace WorkIsland.App;

// The sample is a single row in the empty gap immediately above the text,
// including the displayed glass. No frame capture, image retention or new timer.
internal sealed class BackdropLuminanceSampler : IDisposable
{
    private Drawing.Bitmap? bitmap;
    private Drawing.Graphics? graphics;
    private byte[] pixels=[];
    internal int Samples { get; private set; }
    internal double LastMilliseconds { get; private set; }
    internal bool TryRead(int x,int y,int width,out double luminance)
    {
        luminance=0;var started=Stopwatch.GetTimestamp();
        try
        {
            width=RuntimeCompat.Clamp(width,1,1024);
            if(bitmap?.Width!=width)
            {
                Dispose();bitmap=new(width,1,Imaging.PixelFormat.Format32bppRgb);
                graphics=Drawing.Graphics.FromImage(bitmap);pixels=new byte[width*4];
            }
            graphics!.CopyFromScreen(x,y,0,0,new Drawing.Size(width,1),Drawing.CopyPixelOperation.SourceCopy);
            var data=bitmap!.LockBits(new Drawing.Rectangle(0,0,width,1),Imaging.ImageLockMode.ReadOnly,Imaging.PixelFormat.Format32bppRgb);
            try{Marshal.Copy(data.Scan0,pixels,0,pixels.Length);}finally{bitmap.UnlockBits(data);}
            // 32 evenly spaced local samples. Linear-light luminance, not a
            // mean of gamma-encoded RGB. Extreme local highlights are trimmed.
            luminance=Mean(0,width);Samples++;return true;
        }
        catch(Exception error)when(error is Win32Exception or ExternalException or ArgumentException or InvalidOperationException)
        { return false; }
        finally{LastMilliseconds=RuntimeCompat.GetElapsedTime(started).TotalMilliseconds;}
    }
    private double Mean(int from,int to)
    {
        var values=new double[32];var width=Math.Max(1,to-from);var count=Math.Min(width,values.Length);
        for(var i=0;i<count;i++)
        {
            var at=(from+(int)((i+.5)*width/count))*4;
            values[i]=TextContrastPolicy.Luminance(pixels[at+2],pixels[at+1],pixels[at]);
        }
        Array.Sort(values,0,count);var trim=count/8;double total=0;
        for(var i=trim;i<count-trim;i++)total+=values[i];
        return total/(count-2*trim);
    }
    public void Dispose(){graphics?.Dispose();graphics=null;bitmap?.Dispose();bitmap=null;pixels=[];}
}

internal sealed class TextContrastPolicy
{
    private static readonly double[] linear=Enumerable.Range(0,256).Select(i=>i/255d<=.04045?i/255d/12.92:Math.Pow((i/255d+.055)/1.055,2.4)).ToArray();
    private bool initialized;
    private int pending;
    internal bool DarkInk { get; private set; }
    internal static double Luminance(byte r,byte g,byte b)=>.2126*linear[r]+.7152*linear[g]+.0722*linear[b];
    internal void Reset(){initialized=false;pending=0;DarkInk=false;}
    internal void Observe(double luminance)
    {
        if(!RuntimeCompat.IsFinite(luminance)||luminance<0||luminance>1)return;
        if(!initialized){initialized=true;DarkInk=luminance>.20;pending=0;return;}
        var change=DarkInk?luminance<.16:luminance>.24;
        if(!change){pending=0;return;}
        if(++pending<2)return;
        DarkInk=!DarkInk;pending=0;
    }
    internal Color Title=>DarkInk?Color.FromRgb(32,36,38):Colors.White;
}

public partial class IslandWindow
{
    private readonly TextContrastPolicy textContrast=new();
    private double compactTitleInkWidth;
    private readonly SolidColorBrush compactTitleInk=new(Colors.White);
    private BackdropLuminanceSampler? textBackdropSampler;
    private long lastContrastSample;
    private void RefreshAdaptiveTextContrast()
    {
        if(engine.Data.Settings.BackgroundStyle!="glass"||IsMenuBar||OnboardingActive){textContrast.Reset();return;}
        if(!nativeForegroundEnabled||!IsLoaded||!IsVisible||suspended||fullscreenSuppressed||startupSuppressed||isExpanded||!CompactPanel.IsVisible)return;
        var now=Stopwatch.GetTimestamp();
        if(lastContrastSample!=0&&RuntimeCompat.GetElapsedTime(lastContrastSample,now).TotalSeconds<1)return;
        FrameworkElement? label=TitleLabel.Visibility==Visibility.Visible?TitleLabel:TitleEditor.Visibility==Visibility.Visible?TitleEditor:null;
        if(label is null||label.ActualWidth<=0)return;
        // Both ends use screen pixels so DPI and window scale are accounted for.
        var inkWidth=ReferenceEquals(label,TitleEditor)?label.ActualWidth:Math.Min(label.ActualWidth,compactTitleInkWidth);
        if(inkWidth<=0)return;
        var left=label.PointToScreen(new Point(0,0));var right=label.PointToScreen(new Point(inkWidth,0));
        var x=(int)Math.Ceiling(left.X);var y=(int)Math.Floor(Math.Min(left.Y,right.Y))-3;var width=(int)Math.Floor(right.X)-x;
        if(width<=0)return;
        lastContrastSample=now;textBackdropSampler??=new();
        if(textBackdropSampler.TryRead(x,y,width,out var luminance))textContrast.Observe(luminance);
    }
}
