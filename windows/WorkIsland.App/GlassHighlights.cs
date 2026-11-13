using System.Windows;
using System.Windows.Media;

namespace WorkIsland.App;
public sealed class GlassHighlights : FrameworkElement
{
    protected override void OnRender(DrawingContext drawing)
    {
        var scale = ActualHeight / 92;
        foreach (var (width, strength) in new[] { (3.2, .18), (1.1, .72) })
        {
            var line = width * scale; var inset = line / 2 + .35 * scale;
            var colors = new[] { strength, strength * .7, 0, 0, strength * .5 };
            var positions = new[] { 0, .18, .42, .68, 1 };
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            for (int i = 0; i < colors.Length; i++) brush.GradientStops.Add(new(Color.FromArgb((byte)Math.Round(colors[i] * 255), 255, 255, 255), positions[i]));
            var radius = Math.Max(0, 26 * scale - inset);
            drawing.DrawRoundedRectangle(null, new Pen(brush, line), new Rect(inset, inset, Math.Max(0, ActualWidth - 2 * inset), Math.Max(0, ActualHeight - 2 * inset)), radius, radius);
        }
    }
}
