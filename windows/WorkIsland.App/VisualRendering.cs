using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WorkIsland.Core;
namespace WorkIsland.App;

public sealed class HistoryMatrix : FrameworkElement
{
    public int Columns { get; set; } = 10;
    public int Rows { get; set; } = 1;
    public double CellWidth { get; set; } = 4;
    public double CellHeight { get; set; } = 4;
    public double Gap { get; set; } = 2;
    public double Progress { get; set; }
    public bool BottomUp { get; set; }
    public Color Accent { get; set; } = Color.FromRgb(64,255,97);
    public int FilledCells => Progress <= 0 ? 0 : Progress >= 1 ? Columns * Rows : Math.Clamp((int)(Progress * Columns * Rows),1,Columns * Rows - 1);
    protected override void OnRender(DrawingContext context)
    {
        for (int c=0;c<Columns;c++) for (int r=0;r<Rows;r++)
        {
            var index=BottomUp ? r*Columns+c : c*Rows+r;
            var y=(BottomUp ? Rows-1-r : r)*(CellHeight+Gap);
            var color=Accent;color.A=index<FilledCells ? (byte)184 : (byte)26;
            context.DrawRoundedRectangle(new SolidColorBrush(color),null,new Rect(c*(CellWidth+Gap),y,CellWidth,CellHeight),1.5,1.5);
        }
    }
}
// SuperReminderView scales its canvas to 2x2, so its radius .3+1.3*s
// becomes .15+.65*s in WPF relative coordinates. The existing opacity
// clock supplies both expansion and alpha; no additional timer or clock.
public sealed class ReminderFlash : FrameworkElement
{
    private readonly RadialGradientBrush brush=new()
    {
        Center=new(.5,.5),GradientOrigin=new(.5,.5),
        GradientStops=new()
        {
            new(Color.FromScRgb(.75f,1,1,1),0),
            new(Color.FromScRgb(.16f,1,1,1),.55),
            new(Colors.Transparent,1)
        }
    };
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if(e.Property==OpacityProperty)InvalidateVisual();
    }
    protected override void OnRender(DrawingContext context)
    {
        if(Opacity<=0||RenderSize.Width<=0||RenderSize.Height<=0)return;
        brush.RadiusX=brush.RadiusY=.15+.65*Opacity;
        context.DrawRectangle(brush,null,new Rect(RenderSize));
    }
}
public sealed class ReminderAnimation : DoubleAnimationBase
{
    public double Threshold { get; set; } = 30;
    public double ElapsedOffset { get; set; }
    public bool Preview { get; set; }
    protected override Freezable CreateInstanceCore() => new ReminderAnimation {Threshold=Threshold,ElapsedOffset=ElapsedOffset,Preview=Preview};
    protected override double GetCurrentValueCore(double origin,double destination,AnimationClock clock)
    {
        var elapsed=ElapsedOffset+(clock.CurrentTime?.TotalSeconds??0);
        return Preview?elapsed>=0&&elapsed<1.5?(1-Math.Cos(elapsed/1.5*Math.PI*2))/2:0:ReminderTiming.Intensity(elapsed,Threshold);
    }
}
