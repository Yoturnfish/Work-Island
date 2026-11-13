using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WorkIsland.App;

public partial class IslandWindow
{
    private readonly record struct GlyphTextureStamp(string State,string Material,Size Size,DpiScale Dpi);
    private readonly record struct CellsTextureStamp(long Geometry,Color Color,Size Size,DpiScale Dpi);
    private GlyphTextureStamp? nativeGlyphTextureStamp;
    private CellsTextureStamp? nativeCellsTextureStamp;
    private void UpdateNativePulsePlanes(Size size,DpiScale dpi,bool pulse,bool cellsPulse)
    {
        if(!pulse){nativeGlyphSurface?.SetVisible(false);nativeCellsSurface?.SetVisible(false);return;}
        nativeGlyphSurface??=GoalProgress.CreateNativeForegroundSibling(nativeForegroundSurface!);
        var glyphStamp=new GlyphTextureStamp(visualState,materialSignature,size,dpi);
        if(nativeGlyphTextureStamp!=glyphStamp){UploadPlane(nativeGlyphSurface,CapturePulsePlane(StatusIcon,size,dpi,2));nativeGlyphTextureStamp=glyphStamp;}
        var center=StatusIcon.TransformToAncestor(OuterBorder).Transform(new Point(StatusIcon.ActualWidth/2,StatusIcon.ActualHeight/2));
        nativeGlyphSurface.BreatheScale(statusPulseEpoch,StatusPulseTarget,StatusPulsePeriod,center);nativeGlyphSurface.SetVisible(true);
        if(cellsPulse)
        {
            nativeCellsSurface??=GoalProgress.CreateNativeForegroundSibling(nativeForegroundSurface!);
            var cellsStamp=new CellsTextureStamp(GoalProgress.GeometryVersion,statusBrush.Color,size,dpi);
            if(nativeCellsTextureStamp!=cellsStamp)
            {
                var element=GoalProgress.PulseElement;var opacity=element.Opacity;
                try{element.Opacity=1;UploadPlane(nativeCellsSurface,CapturePulsePlane(element,size,dpi,1));nativeCellsTextureStamp=cellsStamp;}
                finally{element.Opacity=opacity;}
            }
            nativeCellsSurface.BreatheOpacity(GoalProgress.PulseEpoch,GoalProgress.PulseBaseOpacity,.35,2.8);nativeCellsSurface.SetVisible(true);
        }
        else nativeCellsSurface?.SetVisible(false);
    }
    private static void UploadPlane(NativeForegroundSurface surface,BitmapSource bitmap)
    {
        // Preserve captured texel density instead of stretching integer pixel
        // bounds back into a fractional DIP extent.
        surface.Upload(bitmap,new Size(bitmap.PixelWidth*96/bitmap.DpiX,bitmap.PixelHeight*96/bitmap.DpiY));surface.PlaceOnTop();
    }
    private BitmapSource CapturePulsePlane(FrameworkElement element,Size size,DpiScale dpi,int resolution)
    {
        var bitmap=new RenderTargetBitmap(Math.Max(1,(int)Math.Round(size.Width*dpi.DpiScaleX*resolution)),Math.Max(1,(int)Math.Round(size.Height*dpi.DpiScaleY*resolution)),96*dpi.DpiScaleX*resolution,96*dpi.DpiScaleY*resolution,PixelFormats.Pbgra32);
        var visual=new DrawingVisual();
        using(var drawing=visual.RenderOpen())
        {
            var transform=element.TransformToAncestor(OuterBorder);var origin=transform.Transform(new Point());var right=transform.Transform(new Point(1,0));var down=transform.Transform(new Point(0,1));
            drawing.PushTransform(new MatrixTransform(new Matrix(right.X-origin.X,right.Y-origin.Y,down.X-origin.X,down.Y-origin.Y,origin.X,origin.Y)));
            var brush=new VisualBrush(element){Stretch=Stretch.Fill,ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,0,element.ActualWidth,element.ActualHeight)};
            drawing.DrawRectangle(brush,null,new Rect(0,0,element.ActualWidth,element.ActualHeight));drawing.Pop();
        }
        bitmap.Render(visual);bitmap.Freeze();return bitmap;
    }
}
