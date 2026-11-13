using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace WorkIsland.App;

// Keep the orb glow outside its bounds while accepting clicks only inside the painted circle.
public sealed class CircularButton : Button
{
    protected override HitTestResult? HitTestCore(PointHitTestParameters parameters)
    {
        var radiusX=ActualWidth/2;var radiusY=ActualHeight/2;
        if(radiusX<=0||radiusY<=0)return null;
        var x=(parameters.HitPoint.X-radiusX)/radiusX;var y=(parameters.HitPoint.Y-radiusY)/radiusY;
        return x*x+y*y<=1 ? new PointHitTestResult(this,parameters.HitPoint) : null;
    }
}
