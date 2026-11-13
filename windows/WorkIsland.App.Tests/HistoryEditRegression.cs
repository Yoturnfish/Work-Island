using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunHistoryEditChecks(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var saved=JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions);
        var folder=Path.Combine(output,"history-edit");Directory.CreateDirectory(folder);
        var today=DateOnly.Parse(LogicalDay.Key(DateTimeOffset.Now,engine.Zone,0));var day=today.ToString("yyyy-MM-dd");
        Session Record(string id,double seconds)=>new(){ApplicationIdentifier=id,ApplicationName="History editor",DayKey=day,StartedAt=DateTimeOffset.Now.AddHours(-1),EndedAt=DateTimeOffset.Now,EffectiveSeconds=seconds};
        Grid[] EditorRows()=>Field<StackPanel>("HistoryRows").Children.OfType<Grid>().Where(g=>g.Children.OfType<StackPanel>().Any()).ToArray();
        (TextBox Hours,TextBox Minutes) Inputs(){var input=EditorRows()[0].Children.OfType<StackPanel>().Single();return ((TextBox)input.Children[0],(TextBox)input.Children[2]);}
        void Enter(TextBox box)=>box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window)!,0,Key.Enter){RoutedEvent=Keyboard.KeyDownEvent});
        try
        {
            engine.Data.Settings.Rules=[];engine.Data.Settings.DayResetMinutes=0;engine.Data.Settings.ReduceMotion=true;engine.Data.Settings.Language="zh";
            engine.Data.Sessions.Clear();engine.Data.Sessions.Add(Record("EDITOR.EXE",3600));engine.Data.Sessions.Add(Record("editor.exe",600));
            Call("ToggleExpanded",null!,new RoutedEventArgs());Call("OpenHistory",null!,new RoutedEventArgs());Call("ShowHistoryDetail",day);await Task.Delay(100);
            check("Windows history groups executable identifiers without case duplicates",EditorRows().Length==1,new{rows=EditorRows().Length});
            var input=Inputs();input.Hours.Text="1";input.Minutes.Text="90";Enter(input.Hours);
            check("history accepts non-negative overflow minutes like Mac and saves their total",Math.Abs(engine.TodaySeconds(DateTimeOffset.Now)-9000)<.01,engine.TodaySeconds(DateTimeOffset.Now));
            Call("ShowHistoryDetail",day);input=Inputs();input.Hours.Text="+2";input.Minutes.Text="0";var before=engine.TodaySeconds(DateTimeOffset.Now);Enter(input.Hours);
            check("history rejects signed numbers before touching records",Math.Abs(engine.TodaySeconds(DateTimeOffset.Now)-before)<.01,new{before,after=engine.TodaySeconds(DateTimeOffset.Now)});
            foreach(var invalid in new[]{("","0"),("0",""),("-1","0"),("0","+1"),("1.5","0"),("１","0"),("abc","0"),(long.MaxValue.ToString(),"0"),("0",long.MaxValue.ToString())})
            {
                Call("ShowHistoryDetail",day);input=Inputs();input.Hours.Text=invalid.Item1;input.Minutes.Text=invalid.Item2;
                var snapshot=JsonSerializer.Serialize(engine.Data.Sessions,AtomicStore.JsonOptions);Enter(input.Hours);
                check("invalid history input leaves records intact: "+invalid.Item1+"/"+invalid.Item2,JsonSerializer.Serialize(engine.Data.Sessions,AtomicStore.JsonOptions)==snapshot,null);
            }
            Call("ShowHistoryDetail",day);input=Inputs();input.Hours.Text=" 600000 ";input.Minutes.Text="90";Enter(input.Hours);
            check("large valid history totals display without 32-bit wrap and normalize minutes",engine.TodaySeconds(DateTimeOffset.Now)==2160005400&&input.Hours.Text=="600001"&&input.Minutes.Text=="30",new{input.Hours.Text,minutes=input.Minutes.Text});
            check("history commit retains its fresh observation boundary for subsequent work",engine.LastObservation is not null,null);
            typeof(IslandWindow).GetField("historyMode",Flags)!.SetValue(window,"list");typeof(IslandWindow).GetField("historySignature",Flags)!.SetValue(window,"");Call("RefreshHistory");
            string RecentTotal(){var button=(Button)Field<StackPanel>("HistoryRows").Children[1];return ((Grid)((Border)button.Content).Child).Children.OfType<TextBlock>().Last().Text;}
            var recentBefore=RecentTotal();engine.Data.Sessions[0].EffectiveSeconds+=60;Call("Render");
            check("large valid totals continue refreshing the recent history list",RecentTotal()!=recentBefore,new{before=recentBefore,after=RecentTotal()});
            engine.Data.Sessions.Clear();engine.Data.Sessions.Add(Record("editor.exe",60));Call("ShowHistoryStatistics");window.UpdateLayout();await Task.Delay(100);
            var scroll=Field<StackPanel>("HistoryRows").Children.OfType<ScrollViewer>().Single();var strip=(HistoryStatisticsStrip)scroll.Content;
            engine.Data.Sessions[0].EffectiveSeconds=120;Call("Render");window.UpdateLayout();
            check("open statistics track new work without requiring navigation",strip.SecondsOn(today)==120,new{shown=strip.SecondsOn(today),actual=engine.TodaySeconds(DateTimeOffset.Now)});
            check("statistics refresh preserves its controls and scroll ownership",ReferenceEquals(scroll,Field<StackPanel>("HistoryRows").Children.OfType<ScrollViewer>().Single())&&ReferenceEquals(strip.ScrollOwner,scroll),null);
            strip.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window)!,0,Key.End){RoutedEvent=Keyboard.KeyDownEvent});
            engine.Data.Sessions[0].EffectiveSeconds=180;Call("Render");
            check("selected statistics date updates its displayed total while work continues",Field<StackPanel>("HistoryRows").Children.OfType<TextBlock>().Single().Text.Contains("03M"),Field<StackPanel>("HistoryRows").Children.OfType<TextBlock>().Single().Text);
            var sparse=new HistoryStatisticsStrip(new System.Collections.Generic.Dictionary<DateOnly,double>{{today.AddDays(-7305),3600},{today,180}},today,14400,Colors.Green,window.FontFamily);
            sparse.Measure(new Size(240,56));sparse.Arrange(new Rect(0,0,240,56));sparse.SetHorizontalOffset(0);
            sparse.UpdateCurrentDay(today.AddDays(1),0,false);sparse.Measure(new Size(240,56));sparse.Arrange(new Rect(0,0,240,56));
            check("statistics midnight extends range without moving a reader viewing older days",sparse.DayCount==7307&&sparse.LastDay==today.AddDays(1)&&sparse.HorizontalOffset==0,null);
            sparse.SetHorizontalOffset(double.PositiveInfinity);sparse.UpdateCurrentDay(today.AddDays(2),1,true);sparse.Measure(new Size(240,56));sparse.Arrange(new Rect(0,0,240,56));
            check("statistics midnight follows the latest day when already at the end",Math.Abs(sparse.HorizontalOffset-Math.Max(0,sparse.ExtentWidth-sparse.ViewportWidth))<.1&&sparse.SecondsOn(today)==180,null);
            var allocated=GC.GetAllocatedBytesForCurrentThread();var watch=System.Diagnostics.Stopwatch.StartNew();
            for(var i=0;i<1000;i++)sparse.UpdateCurrentDay(today.AddDays(2),i+2,true);
            watch.Stop();var allocatedBytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
            check("live statistics updates remain bounded for twenty years of sparse dates",allocatedBytes<2_000_000&&watch.Elapsed.TotalMilliseconds<500,new{allocatedBytes,elapsedMs=watch.Elapsed.TotalMilliseconds});
            window.UpdateLayout();await Task.Delay(120);Call("SaveScreenRegion",Path.Combine(folder,"statistics-live.png"),23);
            Call("ShowHistoryDetail",day);input=Inputs();
            var store=Field<AtomicStore>("store");Call("Persist");var originalPath=store.Path;
            if(!Path.GetFullPath(originalPath).StartsWith(Path.GetFullPath(output)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("File lock outside isolated output.");
            var bytes=File.ReadAllBytes(originalPath);
            using(var locked=new FileStream(originalPath,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                engine.Data.Sessions.Add(Record("editor.exe",5));var expected=engine.TodaySeconds(DateTimeOffset.Now);
                input.Hours.Text="3";input.Minutes.Text="0";Enter(input.Hours);
                check("failed history save restores live total and stays on detail page",Math.Abs(engine.TodaySeconds(DateTimeOffset.Now)-expected)<.01&&ReferenceEquals(Field<FrameworkElement>("activePage"),Field<StackPanel>("HistoryPage")),new{expected,actual=engine.TodaySeconds(DateTimeOffset.Now),page=Field<FrameworkElement>("activePage").Name});
                check("failed history save leaves primary bytes intact",File.ReadAllBytes(originalPath).SequenceEqual(bytes),null);
                window.UpdateLayout();await Task.Delay(120);Call("SaveScreenRegion",Path.Combine(folder,"save-failure.png"),23);
            }
            // Same row is still usable after storage recovers.
            input.Hours.Text="0";input.Minutes.Text="4";Enter(input.Hours);
            check("history save can retry after the file lock is released",engine.TodaySeconds(DateTimeOffset.Now)==240&&new AtomicStore(originalPath).Load().Sessions.Sum(s=>s.EffectiveSeconds)==240,null);
            input.Hours.Text="0";input.Minutes.Text="0";Enter(input.Hours);
            check("explicit zero clears only the selected total and survives reload",engine.TodaySeconds(DateTimeOffset.Now)==0&&new AtomicStore(originalPath).Load().Sessions.All(s=>s.EffectiveSeconds==0),null);
        }
        finally
        {
            var restored=JsonSerializer.Deserialize<AppState>(saved,AtomicStore.JsonOptions)!;engine.Data.Settings=restored.Settings;engine.Data.Sessions.Clear();engine.Data.Sessions.AddRange(restored.Sessions);engine.ResetObservation();
            typeof(IslandWindow).GetField("historyMode",Flags)!.SetValue(window,"list");Call("ProfileChanged");Call("ShowPage","home",false);Call("CollapseToTimer");
        }
    }
    // This path uses only APIs available in the retained pre-fix binary, so the
    // same harness can compare old/new product DLLs without modifying either.
    private static async Task RunHistorySaveVisualChecks(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");engine.Data.Settings.Rules=[];engine.Data.Settings.ReduceMotion=true;engine.Data.Settings.BackgroundStyle="black";engine.Data.Settings.BackgroundOpacity=1;engine.Data.Settings.Language="zh";
        var now=DateTimeOffset.Now;var day=LogicalDay.Key(now,engine.Zone,engine.Data.Settings.DayResetMinutes);
        engine.Data.Sessions.Clear();engine.Data.Sessions.Add(new(){ApplicationIdentifier="visual-history.exe",ApplicationName="History editor",DayKey=day,StartedAt=now.AddHours(-1),EndedAt=now,EffectiveSeconds=3600});
        Call("ToggleExpanded",null!,new RoutedEventArgs());Call("OpenHistory",null!,new RoutedEventArgs());Call("ShowHistoryDetail",day);window.UpdateLayout();await Task.Delay(180);
        Call("SaveScreenRegion",Path.Combine(output,"before-save.png"),23);
        var row=Field<StackPanel>("HistoryRows").Children.OfType<Grid>().Single(g=>g.Children.OfType<StackPanel>().Any());var input=row.Children.OfType<StackPanel>().Single();var hours=(TextBox)input.Children[0];var minutes=(TextBox)input.Children[2];
        var store=Field<AtomicStore>("store");Call("Persist");var before=File.ReadAllBytes(store.Path);
        using(var locked=new FileStream(store.Path,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            engine.Data.Sessions.Add(new(){ApplicationIdentifier="visual-history.exe",ApplicationName="History editor",DayKey=day,StartedAt=now,EndedAt=now,EffectiveSeconds=5});
            hours.Text="2";minutes.Text="0";hours.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window)!,0,Key.Enter){RoutedEvent=Keyboard.KeyDownEvent});
            window.UpdateLayout();await Task.Delay(180);Call("SaveScreenRegion",Path.Combine(output,"failed-save.png"),23);
            check("actual composed failed-save view stays on history details",Field<FrameworkElement>("activePage").Name=="HistoryPage",Field<FrameworkElement>("activePage").Name);
            check("native visual failure fixture preserves memory and primary bytes",engine.TodaySeconds(now)==3605&&File.ReadAllBytes(store.Path).SequenceEqual(before),null);
        }
        File.WriteAllText(Path.Combine(output,"product-runtime.json"),JsonSerializer.Serialize(new{dll=typeof(IslandWindow).Assembly.Location,sha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),method="Same isolated WPF routed edit event and actual locked primary file; waited for layout and compositor before real screen capture. No external app input."},AtomicStore.JsonOptions));
    }
}
