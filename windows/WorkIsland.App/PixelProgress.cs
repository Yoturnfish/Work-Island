using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Diagnostics;
namespace WorkIsland.App;

// Matches source matrix/wave geometry. Two compositor scan bands, no per-frame timer.
public sealed partial class PixelProgress : Grid
{
    private readonly Path filled = new() { Opacity = 0.72 }, unfilled = new() { Opacity = 0.10 };
    private readonly Canvas filledScan = new(), unfilledScan = new();
    private readonly Rectangle filledBand = new(), unfilledBand = new();
    private readonly TranslateTransform scan = new();
    private AnimationClock? scanClock,pulseClock;
    private long scanEpoch,pulseEpoch;
    private bool nativePulseSuspended;
    private SolidColorBrush accent = new(Color.FromRgb(64, 255, 97));
    private double progress, lastWidth, lastHeight;
    private string state = "away", style = "matrix", mode = "";
    private bool reduced, breathingAllowed = true, accentObserved, unloaded;
    private int lastCount = -1, lastColumns = -1;
    public int Columns { get; private set; } = 30;
    internal long GeometryVersion { get; private set; }
    public bool ScanAnimating => scan.HasAnimatedProperties || nativeScan?.Active==true;
    public bool PulseAnimating => unfilled.HasAnimatedProperties||nativePulseSuspended&&mode=="pulse";
    internal bool NativePulseWanted=>mode=="pulse";
    internal long PulseEpoch=>pulseEpoch;
    internal double PulseBaseOpacity=>style=="matrix"?.10:.18;
    internal FrameworkElement PulseElement=>unfilled;
    public PixelProgress()
    {
        IsHitTestVisible = false;
        Children.Add(unfilled); Children.Add(filled); Children.Add(unfilledScan); Children.Add(filledScan);
        unfilledScan.Children.Add(unfilledBand); filledScan.Children.Add(filledBand);
        filledBand.RenderTransform = scan; unfilledBand.RenderTransform = scan;
        SizeChanged += (_, _) => Rebuild(true);
        IsVisibleChanged+=(_,_)=>ConfigureMotion(false);
        Loaded+=(_,_)=>
        {
            unloaded=false;
            if(!accentObserved){accent.Changed+=AccentChanged;accentObserved=true;}
            BindNativePresentation();
            ConfigureMotion(false);UpdateBandColors();
        };
        Unloaded+=(_,_)=>
        {
            unloaded=true;
            if(accentObserved){accent.Changed-=AccentChanged;accentObserved=false;}
            ConfigureMotion(false);
            ReleaseNativePresentation();
        };
    }
    public void Update(double value, string activity, string progressStyle, SolidColorBrush brush, bool reduceMotion, int columns, bool awayBreathing = true)
    {
        breathingAllowed = awayBreathing;
        progress = Math.Clamp(value, 0, 1); state = activity; reduced = reduceMotion;
        var structureChanged = style != progressStyle || lastColumns != columns;
        style = progressStyle; Columns = columns;
        if (!ReferenceEquals(accent, brush)||!accentObserved) { if (accentObserved) accent.Changed -= AccentChanged; accent = brush; if(!unloaded){accent.Changed += AccentChanged; accentObserved = true;}else accentObserved=false; }
        filled.Fill = accent; unfilled.Fill = accent;
        Rebuild(structureChanged); ConfigureMotion(structureChanged); UpdateBandColors();UpdateNativeColor();
    }
    private void Rebuild(bool force)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var elements = style == "matrix" ? Columns * 3 : 37;
        var count = progress <= 0 ? 0 : progress >= 1 ? elements : style == "matrix" ? (int)(progress * elements) : Math.Clamp((int)(progress * elements), 1, elements - 1);
        if (!force && count == lastCount && lastWidth == ActualWidth && lastHeight == ActualHeight) return;
        GeometryVersion++;
        var a = new GeometryGroup(); var b = new GeometryGroup();
        if (style == "matrix")
        {
            var width = (ActualWidth - (Columns - 1) * 2) / Columns; var height = (ActualHeight - 4) / 3;
            for (var c = 0; c < Columns; c++) for (var r = 0; r < 3; r++)
                (c * 3 + r < count ? a : b).Children.Add(new RectangleGeometry(new Rect(c * (width + 2), r * (height + 2), Math.Max(0, width), Math.Max(0, height)), 1.5, 1.5));
        }
        else
        {
            const int cells = 37; var gap = Math.Min(3, ActualWidth / (cells * 2)); var width = (ActualWidth - (cells - 1) * gap) / cells;
            for (var i = 0; i < cells; i++)
            {
                var height = ActualHeight * (0.32 + 0.68 * Math.Abs(Math.Sin(Math.Min(i, cells - 1 - i) * 7 * Math.PI / (cells - 1))));
                (i < count ? a : b).Children.Add(new RectangleGeometry(new Rect(i * (width + gap), (ActualHeight - height) / 2, width, height), Math.Min(width / 2, 3), Math.Min(width / 2, 3)));
            }
        }
        a.Freeze(); b.Freeze(); filled.Data = a; unfilled.Data = b; filledScan.Clip = a; unfilledScan.Clip = b;
        filledBand.Width = unfilledBand.Width = Math.Max(24, ActualWidth * (style == "matrix" ? .18 : .16));
        filledBand.Height = unfilledBand.Height = ActualHeight;
        UpdateNativeGeometry();
        lastCount = count; lastColumns = Columns;
        var sizeChanged = lastWidth != ActualWidth || lastHeight != ActualHeight;
        lastWidth = ActualWidth; lastHeight = ActualHeight;
        ConfigureMotion(force || sizeChanged); UpdateBandColors();
    }
    private void ConfigureMotion(bool force)
    {
        var next = reduced || !IsVisible || unloaded || ActualWidth<=0 || ActualHeight<=0 ? "static" : state == "away" && progress < 1 && breathingAllowed ? "pulse" : state == "tracking" ? "scan" : "static";
        if (!force && next == mode) return;
        // One monotonic phase across renderers. Using WPF's last presented
        // value as each new epoch loses a fraction of a frame on every switch.
        var phase=mode=="scan"&&next=="scan"?CurrentScanPhase:0;
        if(next=="scan"&&mode!="scan")scanEpoch=Stopwatch.GetTimestamp();
        if(next=="pulse"&&mode!="pulse")pulseEpoch=Stopwatch.GetTimestamp();
        mode = next;scanClock?.Controller?.Remove();pulseClock?.Controller?.Remove();scanClock=pulseClock=null;
        scan.BeginAnimation(TranslateTransform.XProperty, null); unfilled.BeginAnimation(OpacityProperty, null);
        filled.Opacity = style == "matrix" ? 0.72 : 1; unfilled.Opacity = style == "matrix" ? 0.10 : 0.18;
        var native = next == "scan" && StartNativeScan();
        if(!native && nativeScan is not null){nativeScan.SetScanning(false);nativeScan.SetOpacity(nativeGroupOpacity??0);nativePresentation=null;}
        filledScan.Visibility = unfilledScan.Visibility = next == "scan" && !native ? Visibility.Visible : Visibility.Collapsed;
        // The brush may still be interpolating from away orange. Never retain its old color as a green-cell glow.
        filled.Effect = state == "tracking" ? new DropShadowEffect { Color = accent.Color, BlurRadius = 5, ShadowDepth = 0, Opacity = 0.42 } : null;
        if (next == "scan" && !native)
        {
            // Until WPF's next clock tick, display the handoff value as the
            // base value. Seeking against its old idle tick would jump when
            // the time manager catches up after compositor-only rendering.
            scan.X=-filledBand.Width+phase*(ActualWidth+filledBand.Width);
            scanClock=(AnimationClock)new DoubleAnimation(-filledBand.Width,ActualWidth,TimeSpan.FromSeconds(2.8)){RepeatBehavior=RepeatBehavior.Forever,BeginTime=-TimeSpan.FromSeconds(phase*2.8)}.CreateClock(true);
            scan.ApplyAnimationClock(TranslateTransform.XProperty,scanClock);
        }
        else if(next=="pulse")StartWpfPulse();
    }
    private void StartWpfPulse()
    {
        if(nativePulseSuspended||mode!="pulse")return;
        var phase=PulsePhase.At(pulseEpoch,2.8);unfilled.Opacity=PulsePhase.Value(phase,PulseBaseOpacity,.35);
        var animation=new DoubleAnimation(PulseBaseOpacity,.35,TimeSpan.FromSeconds(1.4)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut},BeginTime=-TimeSpan.FromSeconds(phase*2.8)};
        Timeline.SetDesiredFrameRate(animation,12);
        pulseClock=(AnimationClock)animation.CreateClock(true);
        unfilled.ApplyAnimationClock(OpacityProperty,pulseClock);
    }
    internal void SetNativePulseSuspended(bool value)
    {
        if(nativePulseSuspended==value)return;nativePulseSuspended=value;pulseClock?.Controller?.Remove();pulseClock=null;unfilled.BeginAnimation(OpacityProperty,null);unfilled.Opacity=PulseBaseOpacity;
        if(!value)StartWpfPulse();
    }
    private double CurrentScanPhase=>(Stopwatch.GetElapsedTime(scanEpoch).TotalSeconds/2.8)%1;
    private void AccentChanged(object? sender, EventArgs e)
    {
        UpdateBandColors();
        UpdateNativeColor();
        if (filled.Effect is DropShadowEffect glow) glow.Color = accent.Color;
    }
    private void UpdateBandColors()
    {
        if (filledBand.Fill is LinearGradientBrush existing && existing.GradientStops[3].Color.R == accent.Color.R && existing.GradientStops[3].Color.G == accent.Color.G && existing.GradientStops[3].Color.B == accent.Color.B) return;
        LinearGradientBrush Gradient(bool isFilled)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            var strengths = isFilled ? new[] { 0, 0.36, 0.72, 1, 0.72, 0.36, 0 } : new[] { 0, 0.11, 0.22, 0.33, 0.22, 0.11, 0 };
            var positions = new[] { 0, 0.17, 0.33, 0.5, 0.67, 0.83, 1 };
            for (var i = 0; i < strengths.Length; i++) brush.GradientStops.Add(new(Color.FromArgb((byte)(strengths[i] * 255), accent.Color.R, accent.Color.G, accent.Color.B), positions[i]));
            return brush;
        }
        filledBand.Fill = Gradient(true); unfilledBand.Fill = Gradient(false);
    }
}
