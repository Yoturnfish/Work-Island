using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunHistoryFocusReview(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var store=Field<AtomicStore>("store");var settings=engine.Data.Settings;
        settings.HasCompletedOnboarding=true;settings.Paused=true;settings.Rules=[];settings.ReduceMotion=true;settings.Language="zh";settings.BackgroundStyle="black";settings.DayResetMinutes=0;
        var now=DateTimeOffset.Now;var day=LogicalDay.Key(now,engine.Zone,0);engine.Data.Sessions.Clear();
        foreach(var item in new[]{("HISTORY-A.EXE","焦点验收 A",1200d),("history-a.exe","焦点验收 A",2400d),("history-b.exe","对照记录 B",1800d)})
            engine.Data.Sessions.Add(new(){ApplicationIdentifier=item.Item1,ApplicationName=item.Item2,DayKey=day,StartedAt=now.AddHours(-1),EndedAt=now,EffectiveSeconds=item.Item3});
        engine.ResetObservation();Call("Render");store.Save(engine.Data);
        File.WriteAllText(Path.Combine(output,"before.json"),JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions));
        Field<DispatcherTimer>("fullscreenTimer").Stop();Field<DispatcherTimer>("focusReturn").Stop();
        window.ShowInTaskbar=true;window.Title="Work Island · 历史 Tab 焦点验收";window.Left=360;window.Top=260;
        Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenHistory",null,new RoutedEventArgs());Call("ShowHistoryDetail",day);window.UpdateLayout();
        await Task.Delay(100);Field<DispatcherTimer>("focusReturn").Stop();
        TextBox[] Fields()=>Field<StackPanel>("HistoryRows").Children.OfType<Grid>().SelectMany(g=>g.Children.OfType<StackPanel>()).SelectMany(p=>p.Children.OfType<TextBox>()).ToArray();
        var clock=Stopwatch.StartNew();
        void Record(string kind,string? key=null)
        {
            var focused=Keyboard.FocusedElement as DependencyObject;
            var record=new{kind,key,atMs=clock.Elapsed.TotalMilliseconds,active=window.IsActive,
                focusedId=focused is null?null:AutomationProperties.GetAutomationId(focused),focusedName=focused is null?null:AutomationProperties.GetName(focused),
                fields=Fields().Select(f=>new{id=AutomationProperties.GetAutomationId(f),name=AutomationProperties.GetName(f),value=f.Text,keyboardFocused=f.IsKeyboardFocused,caret=f.CaretIndex,selection=f.SelectionLength}).ToArray()};
            var json=JsonSerializer.Serialize(record,AtomicStore.JsonOptions);
            File.AppendAllText(Path.Combine(output,"focus-events.jsonl"),JsonSerializer.Serialize(record)+Environment.NewLine);
            File.WriteAllText(Path.Combine(output,"live-focus.json"),json);
        }
        KeyboardFocusChangedEventHandler gotFocus=(_,_)=>Record("got-keyboard-focus");
        KeyEventHandler keyDown=(_,e)=>Record("preview-key-down",e.Key.ToString());
        KeyEventHandler keyUp=(_,e)=>Record("preview-key-up",e.Key.ToString());
        TextChangedEventHandler textChanged=(_,e)=>{if(e.OriginalSource is TextBox box&&AutomationProperties.GetAutomationId(box).StartsWith("History"))Record("text-changed");};
        window.GotKeyboardFocus+=gotFocus;window.PreviewKeyDown+=keyDown;window.PreviewKeyUp+=keyUp;window.AddHandler(TextBoxBase.TextChangedEvent,textChanged);
        try
        {
            check("passive focus review starts with four distinct synthetic history fields",Fields().Length==4&&Fields().Select(AutomationProperties.GetAutomationId).Distinct().Count()==4,null);
            Record("ready");
            using var process=Process.GetCurrentProcess();
            File.WriteAllText(Path.Combine(output,"ready.json"),JsonSerializer.Serialize(new{processId=process.Id,startedUtcTicks=process.StartTime.ToUniversalTime().Ticks.ToString(),windowHandle=(long)new WindowInteropHelper(window).Handle,day,
                appSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),scope="Passive own-window Keyboard.FocusedElement, IsKeyboardFocused and text/key events. Real keyboard input only through official Computer Use. No focus setting, key injection or input handlers that change product behavior."},AtomicStore.JsonOptions));
            var finish=Path.Combine(output,"finish.txt");
            while(!File.Exists(finish)&&clock.Elapsed.TotalSeconds<600)await Task.Delay(100);
            Record("finished");check("native interaction review completed before its bounded watchdog",File.Exists(finish),null);
            var saved=(bool)Call("PrepareNormalExit")!;check("independent review saves through the normal exit preparation",saved,null);
            File.WriteAllText(Path.Combine(output,"after.json"),JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions));
        }
        finally{window.GotKeyboardFocus-=gotFocus;window.PreviewKeyDown-=keyDown;window.PreviewKeyUp-=keyUp;window.RemoveHandler(TextBoxBase.TextChangedEvent,textChanged);}
    }
}
