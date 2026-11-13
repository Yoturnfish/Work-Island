using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunExitSafetyChecks(string output,Action<string,bool,object?> check)
    {
        var folder=Path.Combine(output,"exit-safety");Directory.CreateDirectory(folder);
        var blocker=Path.Combine(folder,"storage-blocker");File.WriteAllText(blocker,"Owned test fixture: file blocks creating the data directory.");
        var store=new AtomicStore(Path.Combine(blocker,"state.json"));
        var now=DateTimeOffset.Now;var record=new Session{ApplicationIdentifier="exit-fixture.exe",ApplicationName="Exit fixture",StartedAt=now.AddHours(-1),EndedAt=now,DayKey=LogicalDay.Key(now,TimeZoneInfo.Local,0),EffectiveSeconds=3600};
        var state=new AppState{Settings=new(){Rules=[]},Sessions=[record]};
        var window=new IslandWindow(store,state,false,null,startupRegistration:new IsolatedStartupRegistration(Path.Combine(folder,"startup.json"),"isolated"),fullscreenSource:new FullscreenFixture());
        window.ShowActivated=false;window.Topmost=false;var closed=false;window.Closed+=(_,_)=>closed=true;
        window.Loaded+=(_,_)=>((DispatcherTimer)typeof(IslandWindow).GetField("timer",Flags)!.GetValue(window)!).Stop();
        try
        {
            window.Show();await Task.Delay(100);
            typeof(IslandWindow).GetMethod("ExitIsland",Flags)!.Invoke(window,[null,new RoutedEventArgs()]);
            check("failed save prevents normal exit and retains memory records",!closed&&!(bool)typeof(IslandWindow).GetField("exiting",Flags)!.GetValue(window)!&&state.Sessions.Contains(record),null);
            var message=(TextBlock)typeof(IslandWindow).GetField("MessageLabel",Flags)!.GetValue(window)!;
            check("failed exit shows concise actionable save error without raw storage paths",!closed&&window.IsVisible&&message.Text.Contains("保存失败")&&message.Text.Contains("重试退出")&&!message.Text.Contains(folder),null);
            if(closed)return; // Old implementation closes here; retain a truthful before report.
            typeof(IslandWindow).GetMethod("SaveImage2x",Flags|BindingFlags.DeclaredOnly)!.Invoke(window,[Path.Combine(folder,"save-failure-2x.png")]);
            typeof(IslandWindow).GetMethod("ShowPage",Flags)!.Invoke(window,["history",false]);
            typeof(IslandWindow).GetMethod("Tick",Flags)!.Invoke(window,null);
            check("repeated background save failures do not kick history back to settings",typeof(IslandWindow).GetField("activePage",Flags)!.GetValue(window) is FrameworkElement{Name:"HistoryPage"},null);
            File.Move(blocker,blocker+".fixture-backup");Directory.CreateDirectory(blocker);
            typeof(IslandWindow).GetMethod("ExitIsland",Flags)!.Invoke(window,[null,new RoutedEventArgs()]);
            var saved=store.Load();
            check("retry after storage recovery saves and exits normally",closed&&saved.Sessions.Exists(s=>s.Id==record.Id&&s.EffectiveSeconds==3600),null);
        }
        finally{if(!closed){typeof(IslandWindow).GetField("exiting",Flags)!.SetValue(window,true);window.Close();}}
        var stableFolder=Path.Combine(folder,"previously-saved");Directory.CreateDirectory(stableFolder);
        var stableStore=new AtomicStore(Path.Combine(stableFolder,"state.json"));var stableData=new AppState{Settings=new(){Rules=[],WindowX=120,WindowY=80}};
        var stable=new IslandWindow(stableStore,stableData,false,null,startupRegistration:new IsolatedStartupRegistration(Path.Combine(folder,"stable-startup.json"),"isolated"),fullscreenSource:new FullscreenFixture());
        stable.ShowActivated=false;stable.Topmost=false;var stableClosed=false;stable.Closed+=(_,_)=>stableClosed=true;
        stable.Loaded+=(_,_)=>((DispatcherTimer)typeof(IslandWindow).GetField("timer",Flags)!.GetValue(stable)!).Stop();
        var held=stableFolder+".held";
        try
        {
            stable.Show();await Task.Delay(100);
            var cached=(string?)typeof(IslandWindow).GetField("lastSaved",Flags)!.GetValue(stable);
            check("exit fixture starts with unchanged successfully saved data",cached==System.Text.Json.JsonSerializer.Serialize(stableData,AtomicStore.JsonOptions),null);
            var scope=Path.GetFullPath(folder)+Path.DirectorySeparatorChar;
            if(!Path.GetFullPath(stableFolder).StartsWith(scope,StringComparison.OrdinalIgnoreCase)||!Path.GetFullPath(held).StartsWith(scope,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Fixture move outside scope.");
            Directory.Move(stableFolder,held);File.WriteAllText(stableFolder,"Owned fixture: persisted target became unavailable.");
            typeof(IslandWindow).GetMethod("ExitIsland",Flags)!.Invoke(stable,[null,new RoutedEventArgs()]);
            check("normal exit verifies durable write even when serialized cache is unchanged",!stableClosed,null);
            File.Move(stableFolder,stableFolder+".fixture-backup");Directory.Move(held,stableFolder);
            if(!stableClosed)typeof(IslandWindow).GetMethod("ExitIsland",Flags)!.Invoke(stable,[null,new RoutedEventArgs()]);
            check("unchanged-data exit succeeds after target recovers",stableClosed&&File.Exists(stableStore.Path),null);
        }
        finally{if(!stableClosed){typeof(IslandWindow).GetField("exiting",Flags)!.SetValue(stable,true);stable.Close();}}
    }
}
