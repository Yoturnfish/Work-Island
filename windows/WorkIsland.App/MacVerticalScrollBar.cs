using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace WorkIsland.App;

// Mac PersistentScrollbarMetrics: the minimum knob is part of both layout and
// drag geometry, rather than a MinHeight painted outside WPF Track's allocation.
public sealed class MacVerticalScrollBar : ScrollBar
{
    private Thumb? knob;
    private double dragValue;
    public double KnobLength { get; private set; }
    public double KnobOffset { get; private set; }

    public override void OnApplyTemplate()
    {
        if (knob is not null)
        {
            knob.DragStarted -= StartDrag;
            knob.DragDelta -= DragKnob;
        }
        base.OnApplyTemplate();
        knob = GetTemplateChild("MacScrollKnob") as Thumb;
        if (knob is not null)
        {
            knob.DragStarted += StartDrag;
            knob.DragDelta += DragKnob;
        }
        UpdateKnob();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateKnob();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ValueProperty || e.Property == MinimumProperty
            || e.Property == MaximumProperty || e.Property == ViewportSizeProperty)
            UpdateKnob();
    }

    private void UpdateKnob()
    {
        var track = Math.Max(0, ActualHeight);
        var range = Math.Max(0, Maximum - Minimum);
        var content = Math.Max(1, range + ViewportSize);
        KnobLength = Math.Min(track, Math.Max(18, track * ViewportSize / content));
        KnobOffset = range > 0 ? (track - KnobLength) * Math.Clamp((Value - Minimum) / range, 0, 1) : 0;
        if (knob is null) return;
        knob.Height = KnobLength;
        knob.Margin = new Thickness(0, KnobOffset, 0, 0);
    }

    private void StartDrag(object sender, DragStartedEventArgs e) => dragValue = Value;

    private void DragKnob(object sender, DragDeltaEventArgs e)
    {
        var travel = ActualHeight - KnobLength;
        if (travel > 0)
        {
            dragValue = Math.Clamp(dragValue + e.VerticalChange * (Maximum - Minimum) / travel, Minimum, Maximum);
            ScrollTo(dragValue);
        }
        e.Handled = true;
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var y = e.GetPosition(this).Y;
        if (y < KnobOffset || y > KnobOffset + KnobLength)
        {
            ScrollTo(Value + (y < KnobOffset ? -1 : 1) * Math.Max(24, ViewportSize * .75));
            e.Handled = true;
        }
        base.OnPreviewMouseLeftButtonDown(e);
    }

    private void ScrollTo(double value)
    {
        value = Math.Clamp(value, Minimum, Maximum);
        if (TemplatedParent is ScrollViewer viewer) viewer.ScrollToVerticalOffset(value);
        else SetCurrentValue(ValueProperty, value);
    }
}
