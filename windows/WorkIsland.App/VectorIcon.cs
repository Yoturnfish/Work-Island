using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WorkIsland.App;
internal static class VectorIcon
{
    public static FrameworkElement Make(string name, Brush? brush = null, double size = 16)
    {
        var data = name switch
        {
            "chart" => "M 1,8 Q 1,7 2,7 L 5,7 Q 6,7 6,8 L 6,17 L 1,17 Z M 8,4 Q 8,3 9,3 L 12,3 Q 13,3 13,4 L 13,17 L 8,17 Z M 15,1 Q 15,0 16,0 L 19,0 Q 20,0 20,1 L 20,17 L 15,17 Z",
            "power" => "M 10,1 L 10,9 M 5,4 C -2,9 2,18 10,18 C 18,18 22,9 15,4",
            "trash" => "M 4,5 L 16,5 L 15,18 L 5,18 Z M 2,3 L 18,3 M 7,3 L 7,1 L 13,1 L 13,3 M 8,7 L 8,15 M 12,7 L 12,15",
            "chevron" => "M 5,2 L 13,10 L 5,18",
            "plus" => "M 10,2 L 10,18 M 2,10 L 18,10",
            "recognize" => "M 1,6 L 1,1 L 6,1 M 14,1 L 19,1 L 19,6 M 19,14 L 19,19 L 14,19 M 6,19 L 1,19 L 1,14 M 5,10 L 9,14 L 15,6",
            _ => "M 0,0"
        };
        return new Path { Data = Geometry.Parse(data), Width = size, Height = size, Stretch = Stretch.Uniform,
            Fill = name == "chart" ? brush ?? Brushes.White : null,
            Stroke = name == "chart" ? null : brush ?? Brushes.White, StrokeThickness = 1.8,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    }
}
public partial class IslandWindow
{
    private void AnimateSwitch(object sender, RoutedEventArgs args)
    {
        if (sender is not CheckBox check || check.Template?.FindName("ToggleTrack", check) is not Border track
            || check.Template.FindName("ToggleThumb", check) is not Ellipse thumb) return;
        var enabled = check.IsChecked == true;
        var target = enabled ? Color.FromRgb(64, 255, 97) : Color.FromArgb(20, 255, 255, 255);
        var prior = (track.Background as SolidColorBrush)?.Color ?? target;
        var brush = new SolidColorBrush(target); track.Background = brush;
        var shift = thumb.RenderTransform as TranslateTransform ?? new TranslateTransform(); thumb.RenderTransform = shift;
        var animate = args.RoutedEvent != FrameworkElement.LoadedEvent && engine is not null
            && !engine.Data.Settings.ReduceMotion && SystemParameters.ClientAreaAnimation;
        if (animate)
        {
            var ease = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut };
            brush.BeginAnimation(SolidColorBrush.ColorProperty, new System.Windows.Media.Animation.ColorAnimation(prior, target, TimeSpan.FromSeconds(.18)));
            shift.BeginAnimation(TranslateTransform.XProperty, new System.Windows.Media.Animation.DoubleAnimation(shift.X, enabled ? 14 : 0, TimeSpan.FromSeconds(.18)) { EasingFunction = ease });
        }
        else { shift.BeginAnimation(TranslateTransform.XProperty, null); shift.X = enabled ? 14 : 0; }
    }
}
