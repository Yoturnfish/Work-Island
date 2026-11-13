using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunHistoryPerformanceChecks(IslandWindow window,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|System.Reflection.BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var saved=JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions);
        int Elements(DependencyObject root){var count=1;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)count+=Elements(VisualTreeHelper.GetChild(root,i));return count;}
        try
        {
            var today=DateOnly.Parse(LogicalDay.Key(DateTimeOffset.Now,engine.Zone,engine.Data.Settings.DayResetMinutes));
            engine.Data.Sessions.Clear();
            var now=DateTimeOffset.Now;
            foreach(var offset in new[]{-7305,-3652,0})engine.Data.Sessions.Add(new(){ApplicationIdentifier="history-perf.exe",ApplicationName="Performance fixture",DayKey=today.AddDays(offset).ToString("yyyy-MM-dd"),StartedAt=now.AddDays(offset),EndedAt=now.AddDays(offset).AddHours(1),EffectiveSeconds=3600});
            Call("ToggleExpanded",null,new RoutedEventArgs());await Task.Delay(300);Call("OpenHistory",window,new RoutedEventArgs());await Task.Delay(300);
            var allocation=TestRuntime.GetTotalAllocatedBytes(true);var watch=Stopwatch.StartNew();Call("ShowHistoryStatistics");window.UpdateLayout();watch.Stop();
            var rows=Field<StackPanel>("HistoryRows");var scroller=rows.Children.OfType<ScrollViewer>().Single();var elements=Elements(scroller);
            var evidence=new{days=7306,visualElements=elements,elapsedMs=watch.Elapsed.TotalMilliseconds,allocatedBytes=TestRuntime.GetTotalAllocatedBytes(true)-allocation};
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Field<AtomicStore>("store").Path)!,"history-performance.json"),JsonSerializer.Serialize(evidence,AtomicStore.JsonOptions));
            check("twenty years of history use bounded realized visuals",elements<100,evidence);
            check("twenty-year statistics open within 500ms",watch.Elapsed.TotalMilliseconds<500,evidence);
            var strip=(HistoryStatisticsStrip)scroller.Content;
            await Task.Delay(80);
            check("statistics initially reach latest logical day after layout",strip.HorizontalOffset>100000&&Math.Abs(strip.HorizontalOffset-Math.Max(0,strip.ExtentWidth-strip.ViewportWidth))<.1,null);
            check("statistics draw only visible dates while preserving full date range",strip.DayCount==7306&&strip.FirstDay==today.AddDays(-7305)&&strip.LastDay==today&&strip.RenderedDayCount>0&&strip.RenderedDayCount<20,null);
            scroller.ScrollToHorizontalOffset(0);window.UpdateLayout();await Task.Delay(50);
            scroller.ScrollToHorizontalOffset(scroller.ScrollableWidth);window.UpdateLayout();await Task.Delay(50);
            check("native ScrollViewer connects to full sparse history extent",ReferenceEquals(strip.ScrollOwner,scroller)&&scroller.ScrollableWidth>100000&&strip.HorizontalOffset>100000,null);
            scroller.ScrollToHorizontalOffset(0);window.UpdateLayout();await Task.Delay(50);
            check("oldest dates remain scrollable with exact totals and missing-day zeros",strip.HorizontalOffset==0&&strip.SecondsOn(today.AddDays(-7305))==3600&&strip.SecondsOn(today.AddDays(-7304))==0,null);
            Call("SaveImage2x",Path.Combine(Path.GetDirectoryName(Field<AtomicStore>("store").Path)!,"history-oldest-20years-2x.png"));
            strip.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,System.Windows.Input.Key.End){RoutedEvent=System.Windows.Input.Keyboard.KeyDownEvent});
            check("statistics keyboard End reaches latest extent and announces last date",Math.Abs(strip.HorizontalOffset-Math.Max(0,strip.ExtentWidth-strip.ViewportWidth))<.1&&System.Windows.Automation.AutomationProperties.GetName(strip).StartsWith(today.ToString("yyyy-MM-dd")),null);
            await Task.Delay(60);Call("SaveImage2x",Path.Combine(Path.GetDirectoryName(Field<AtomicStore>("store").Path)!,"history-latest-20years-2x.png"));
            strip.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,System.Windows.Input.Key.Home){RoutedEvent=System.Windows.Input.Keyboard.KeyDownEvent});
            check("statistics keyboard Home restores oldest date and hover total",strip.HorizontalOffset==0&&rows.Children.OfType<TextBlock>().Single().Text.StartsWith(today.AddDays(-7305).ToString("yyyy-MM-dd")),null);
            var sparse=new HistoryStatisticsStrip(new Dictionary<DateOnly,double>{{DateOnly.MinValue,3600},{today,120}},today,14400,Colors.Green,window.FontFamily);
            sparse.Measure(new Size(200,56));sparse.Arrange(new Rect(0,0,200,56));sparse.SetHorizontalOffset(double.PositiveInfinity);
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(200,56,96,96,PixelFormats.Pbgra32);bitmap.Render(sparse);
            check("extreme valid date range remains sparse and viewport bounded",sparse.DayCount==today.DayNumber+1&&sparse.RenderedDayCount<=13&&sparse.RenderedDayCount>0,null);
            sparse.SetHorizontalOffset(double.NaN);sparse.SetHorizontalOffset(-100);
            check("statistics reject NaN and clamp negative scroll",sparse.HorizontalOffset==0,null);
        }
        finally
        {
            var restored=JsonSerializer.Deserialize<AppState>(saved,AtomicStore.JsonOptions)!;engine.Data.Settings=restored.Settings;
            engine.Data.Sessions.Clear();engine.Data.Sessions.AddRange(restored.Sessions);engine.ResetObservation();
            typeof(IslandWindow).GetField("historyMode",Flags)!.SetValue(window,"list");Call("ShowPage","home",false);Call("CollapseToTimer");Call("ProfileChanged");await Task.Delay(300);
        }
    }
}
