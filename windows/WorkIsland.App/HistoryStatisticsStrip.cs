using WorkIsland.Core;
using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace WorkIsland.App;

// Retain the complete logical date range, but draw only the viewport. No daily
// controls, containers or dense zero-filled arrays are allocated for old history.
public sealed class HistoryStatisticsStrip : FrameworkElement, IScrollInfo
{
    private const double Pitch=18, ColumnWidth=12, MatrixHeight=34.5;
    private readonly Dictionary<DateOnly,double> totals;
    private readonly double goal;
    private readonly SolidColorBrush active, inactive;
    private readonly Typeface typeface;
    private int selected=-1;
    private bool announcing,followLatestAfterArrange;
    public DateOnly? FirstDay { get;private set; }
    public DateOnly LastDay { get;private set; }
    public int DayCount { get;private set; }
    public int RenderedDayCount { get; private set; }
    public event Action<DateOnly?,double>? HoverChanged;

    public HistoryStatisticsStrip(IReadOnlyDictionary<DateOnly,double> totals,DateOnly latest,double goal,Color accent,FontFamily font)
    {
        this.totals=totals.ToDictionary(item=>item.Key,item=>item.Value);this.goal=goal;LastDay=latest;
        var first=totals.Keys.Where(date=>date<=latest).Order().Take(1).ToArray();
        if(first.Length>0){FirstDay=first[0];DayCount=latest.DayNumber-first[0].DayNumber+1;}
        active=new(accent){Opacity=184/255d};active.Freeze();inactive=new(accent){Opacity=26/255d};inactive.Freeze();
        typeface=new(font,FontStyles.Normal,FontWeights.Medium,FontStretches.Normal);
        Focusable=true;ClipToBounds=true;Height=56;
    }
    public double SecondsOn(DateOnly date)=>totals.TryGetValue(date,out var seconds)?seconds:0;
    public void UpdateCurrentDay(DateOnly latest,double seconds,bool hasRecords)
    {
        if(!RuntimeCompat.IsFinite(seconds)||seconds<0)throw new ArgumentOutOfRangeException(nameof(seconds));
        var changed=LastDay!=latest||hasRecords&&(!totals.TryGetValue(latest,out var old)||old!=seconds)||!hasRecords&&totals.ContainsKey(latest);
        if(!changed)return;
        var atLatest=Math.Abs(HorizontalOffset-Math.Max(0,ExtentWidth-ViewportWidth))<.1;
        if(hasRecords)totals[latest]=seconds;else totals.Remove(latest);
        // Ordinary work only changes today's value. Do not sort decades of
        // dates or invalidate layout for each observation.
        if(LastDay==latest&&FirstDay is {} existingFirst&&(hasRecords||existingFirst!=latest))
        {
            if(announcing)Announce(selected);InvalidateVisual();return;
        }
        var first=totals.Keys.Where(date=>date<=latest).Order().Take(1).ToArray();
        var previousFirst=FirstDay;var previousLast=LastDay;
        var announcedDate=FirstDay is {} start&&selected>=0?start.AddDays(selected):(DateOnly?)null;
        FirstDay=first.Length>0?first[0]:null;LastDay=latest;DayCount=FirstDay is {} oldest?latest.DayNumber-oldest.DayNumber+1:0;
        if(announcedDate is {} date&&FirstDay is {} origin)selected=date.DayNumber-origin.DayNumber;
        if(previousFirst!=FirstDay||previousLast!=LastDay){followLatestAfterArrange=atLatest;InvalidateMeasure();}
        if(announcing)Announce(selected);
        InvalidateVisual();
    }
    protected override AutomationPeer OnCreateAutomationPeer()=>new FrameworkElementAutomationPeer(this);
    protected override Size MeasureOverride(Size available)=>new(RuntimeCompat.IsFinite(available.Width)?available.Width:200,56);
    protected override Size ArrangeOverride(Size final)
    {
        ViewportWidth=final.Width;ViewportHeight=final.Height;
        ExtentWidth=DayCount==0?0:DayCount*Pitch+Math.Max(0,(ViewportWidth-ColumnWidth)/2);
        SetHorizontalOffset(followLatestAfterArrange?double.PositiveInfinity:HorizontalOffset);followLatestAfterArrange=false;ScrollOwner?.InvalidateScrollInfo();return final;
    }
    protected override void OnRender(DrawingContext context)
    {
        context.DrawRectangle(Brushes.Transparent,null,new Rect(RenderSize));
        RenderedDayCount=0;if(FirstDay is not {} first || DayCount==0)return;
        var begin=RuntimeCompat.Clamp((int)Math.Floor(HorizontalOffset/Pitch),0,DayCount-1);
        var end=RuntimeCompat.Clamp((int)Math.Ceiling((HorizontalOffset+ActualWidth)/Pitch),0,DayCount-1);
        var matrixY=Math.Max(0,ActualHeight-45.5);
        for(var index=begin;index<=end;index++)
        {
            var date=first.AddDays(index);var progress=RuntimeCompat.Clamp(SecondsOn(date)/goal,0,1);
            var lit=progress<=0?0:progress>=1?16:RuntimeCompat.Clamp((int)(progress*16),1,15);
            var x=index*Pitch-HorizontalOffset;
            for(var row=0;row<8;row++)for(var col=0;col<2;col++)
                context.DrawRoundedRectangle(row*2+col<lit?active:inactive,null,new Rect(x+col*6.75,matrixY+(7-row)*4.5,5.25,3),1.5,1.5);
            var label=new FormattedText(date.Day.ToString("00"),CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,typeface,7,Brushes.White,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            context.DrawText(label,new Point(x+(ColumnWidth-label.Width)/2,ActualHeight-9));
            RenderedDayCount++;
        }
    }
    private void Announce(int index)
    {
        if(FirstDay is not {} first || index<0 || index>=DayCount){announcing=false;HoverChanged?.Invoke(null,0);return;}
        announcing=true;
        selected=index;var date=first.AddDays(index);var seconds=SecondsOn(date);
        AutomationProperties.SetName(this,$"{date:yyyy-MM-dd} · {AccessibleDuration(seconds)}");HoverChanged?.Invoke(date,seconds);
    }
    private static string AccessibleDuration(double seconds)
    {
        if(seconds<TimeSpan.MaxValue.TotalSeconds)return TimeSpan.FromSeconds(seconds).ToString("c",CultureInfo.InvariantCulture);
        // Valid manual totals can exceed TimeSpan's tick-based range. Format
        // their represented whole seconds without narrowing or changing data.
        var whole=new BigInteger(seconds);
        return FormattableString.Invariant($"{whole/3600}:{(int)(whole/60%60):00}:{(int)(whole%60):00}");
    }
    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);var x=args.GetPosition(this).X+HorizontalOffset;
        Announce(x%Pitch<ColumnWidth?(int)(x/Pitch):-1);
    }
    protected override void OnMouseLeave(MouseEventArgs args){base.OnMouseLeave(args);announcing=false;HoverChanged?.Invoke(null,0);}
    protected override void OnKeyDown(KeyEventArgs args)
    {
        base.OnKeyDown(args);if(DayCount==0)return;
        if(args.Key is not (Key.Left or Key.Right or Key.Home or Key.End))return;
        var index=selected>=0?selected:DayCount-1;
        index=args.Key switch{Key.Left=>Math.Max(0,index-1),Key.Right=>Math.Min(DayCount-1,index+1),Key.Home=>0,_=>DayCount-1};
        var left=index*Pitch;
        if(args.Key==Key.End)SetHorizontalOffset(double.PositiveInfinity);
        else if(args.Key==Key.Home)SetHorizontalOffset(0);
        else if(left<HorizontalOffset)SetHorizontalOffset(left);else if(left+ColumnWidth>HorizontalOffset+ViewportWidth)SetHorizontalOffset(left+ColumnWidth-ViewportWidth);
        Announce(index);args.Handled=true;
    }
    public bool CanHorizontallyScroll { get;set; }=true;
    public bool CanVerticallyScroll { get;set; }
    public double ExtentWidth { get;private set; }
    public double ExtentHeight=>ViewportHeight;
    public double ViewportWidth { get;private set; }
    public double ViewportHeight { get;private set; }
    public double HorizontalOffset { get;private set; }
    public double VerticalOffset=>0;
    public ScrollViewer ScrollOwner { get;set; }=null!;
    public void SetHorizontalOffset(double offset)
    {
        if(double.IsNaN(offset))return;
        var value=RuntimeCompat.Clamp(offset,0,Math.Max(0,ExtentWidth-ViewportWidth));
        if(value==HorizontalOffset)return;HorizontalOffset=value;InvalidateVisual();ScrollOwner?.InvalidateScrollInfo();
    }
    public void SetVerticalOffset(double offset) { }
    public void LineLeft()=>SetHorizontalOffset(HorizontalOffset-Pitch);
    public void LineRight()=>SetHorizontalOffset(HorizontalOffset+Pitch);
    public void MouseWheelLeft()=>LineLeft();public void MouseWheelRight()=>LineRight();
    public void MouseWheelUp()=>LineLeft();public void MouseWheelDown()=>LineRight();
    public void PageLeft()=>SetHorizontalOffset(HorizontalOffset-ViewportWidth);
    public void PageRight()=>SetHorizontalOffset(HorizontalOffset+ViewportWidth);
    public void LineUp(){ }public void LineDown(){ }public void PageUp(){ }public void PageDown(){ }
    public Rect MakeVisible(Visual visual,Rect rectangle)
    {
        if(visual!=this || rectangle.IsEmpty)return Rect.Empty;
        var left=rectangle.Left+HorizontalOffset;
        if(left<HorizontalOffset)SetHorizontalOffset(left);
        else if(left+rectangle.Width>HorizontalOffset+ViewportWidth)SetHorizontalOffset(left+rectangle.Width-ViewportWidth);
        return rectangle;
    }
}
