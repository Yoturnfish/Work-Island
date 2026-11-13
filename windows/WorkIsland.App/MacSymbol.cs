using System.Windows;
using System.Windows.Media;

namespace WorkIsland.App;

// Vector contours follow the bundled Mac captures and their SF symbol names.
// The font point size is not the visible ink size: retain each symbol's optical
// proportions inside the same slot, rather than stretching all outlines to it.
public sealed class MacSymbol : FrameworkElement
{
    internal const string CupPath="F0 M 3,6 C 3,2.4 15.4,2.4 15.4,6 L 15.4,11 C 15.4,16.3 3,16.3 3,11 Z M 4.6,5.8 C 4.6,3.7 13.8,3.7 13.8,5.8 C 13.8,7.9 4.6,7.9 4.6,5.8 Z M 15.1,5.9 C 21,4.8 21.1,12.3 15.1,12.7 L 15.1,10.8 C 18.5,10.5 18.7,7.3 15.1,7.7 Z M .1,13.1 C 2.2,17.2 15.7,18.3 19.7,13.2 L 19.7,15.2 C 16.4,20.2 3.5,20.2 .1,15.2 Z";
    public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(
        nameof(Symbol), typeof(string), typeof(MacSymbol), new FrameworkPropertyMetadata("gearshape.fill", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty InkProperty = DependencyProperty.Register(
        nameof(Ink), typeof(Brush), typeof(MacSymbol), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public string Symbol { get => (string)GetValue(SymbolProperty); set => SetValue(SymbolProperty, value); }
    public Brush Ink { get => (Brush)GetValue(InkProperty); set => SetValue(InkProperty, value); }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var (width, height) = Symbol switch
        {
            "cup.and.saucer.fill" => (16.4, 16.0),
            "clock.arrow.circlepath" => (22.0, 21.0),
            "square.grid.2x2" => (18.4, 18.4),
            "backward.end.fill" => (14.5, 16.0),
            "pause.fill" => (13.5, 18.0),
            "play.fill" => (15.0, 18.0),
            _ => (20.8, 20.8)
        };
        // AppKit uses a +0.5 upward optical offset for the cup; WPF Y is down.
        var cup = Symbol == "cup.and.saucer.fill";
        drawing.PushTransform(new TranslateTransform((ActualWidth - width) / 2 - (cup ? .5 : 0), (ActualHeight - height) / 2 - (cup ? .5 : 0)));
        drawing.PushTransform(new ScaleTransform(width / 20, height / 20));
        var pen = new Pen(Ink, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        switch (Symbol)
        {
            case "gearshape.fill":
                var gear = new GeometryGroup { FillRule = FillRule.Nonzero };
                gear.Children.Add(new EllipseGeometry(new Point(10, 10), 7.6, 7.6));
                for (var i = 0; i < 8; i++)
                    gear.Children.Add(new RectangleGeometry(new Rect(8, 0, 4, 5), .9, .9) { Transform = new RotateTransform(i * 45, 10, 10) });
                drawing.DrawGeometry(Ink, null, new CombinedGeometry(GeometryCombineMode.Exclude, gear, new EllipseGeometry(new Point(10, 10), 3.1, 3.1)));
                break;
            case "clock.arrow.circlepath":
                drawing.DrawGeometry(null, pen, Geometry.Parse("M 3.2,6.2 A 8.2,8.2 0 1 1 3.1,13.8 M 10.6,5.4 L 10.6,10.4 L 13.8,12.3"));
                drawing.DrawGeometry(Ink, null, Geometry.Parse("M .1,6.1 L 5.8,6.1 L 3,10.1 Z"));
                break;
            case "square.grid.2x2":
                pen.Thickness = 1.65;
                foreach (var x in new[] { 1.1, 12.0 })
                    foreach (var y in new[] { 1.1, 12.0 })
                        drawing.DrawRoundedRectangle(null, pen, new Rect(x, y, 6.9, 6.9), 1.1, 1.1);
                break;
            case "cup.and.saucer.fill":
                drawing.DrawGeometry(Ink, null, Geometry.Parse(CupPath));
                break;
            case "backward.end.fill":
                drawing.DrawRoundedRectangle(Ink, null, new Rect(.2, .3, 2.9, 19.4), .6, .6);
                drawing.DrawGeometry(Ink, null, Geometry.Parse("M 5,10 L 19.5,.3 Q 20,-.2 20,.6 L 20,19.4 Q 20,20.2 19.5,19.7 Z"));
                break;
            case "pause.fill":
                drawing.DrawRoundedRectangle(Ink, null, new Rect(0, 0, 7.0, 20), 1, 1);
                drawing.DrawRoundedRectangle(Ink, null, new Rect(13, 0, 7.0, 20), 1, 1);
                break;
            case "play.fill":
                drawing.DrawGeometry(Ink, null, Geometry.Parse("M 1,.7 Q 1,-.4 2.2,.3 L 19.2,9.2 Q 20.7,10 19.2,10.8 L 2.2,19.7 Q 1,20.4 1,19.3 Z"));
                break;
        }
        drawing.Pop(); drawing.Pop();
    }
}

public partial class IslandWindow
{
    // Mac Color.primary in the captured dark appearance is softer than opaque
    // white. Glass explicitly overrides it to white in SubpageForegroundStyle.
    private static readonly Brush BlackNavigationInk = new SolidColorBrush(Color.FromArgb(224, 255, 255, 255));
    public static readonly DependencyProperty NavigationForegroundProperty = DependencyProperty.Register(
        nameof(NavigationForeground), typeof(Brush), typeof(IslandWindow), new PropertyMetadata(BlackNavigationInk));
    public static readonly DependencyProperty NavigationContentOpacityProperty = DependencyProperty.Register(
        nameof(NavigationContentOpacity), typeof(double), typeof(IslandWindow), new PropertyMetadata(.82));
    public Brush NavigationForeground { get => (Brush)GetValue(NavigationForegroundProperty); set => SetValue(NavigationForegroundProperty, value); }
    public double NavigationContentOpacity { get => (double)GetValue(NavigationContentOpacityProperty); set => SetValue(NavigationContentOpacityProperty, value); }

    private void RefreshNavigationPalette()
    {
        var glass = engine.Data.Settings.BackgroundStyle == "glass";
        NavigationForeground = glass ? Brushes.White : BlackNavigationInk;
        NavigationContentOpacity = glass ? 1 : .82;
    }
}
