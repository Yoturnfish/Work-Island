using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunPreferenceSaveLive(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        void Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var engine=Field<TrackingEngine>("engine");var store=Field<AtomicStore>("store");var settings=engine.Data.Settings;
        settings.Paused=true;settings.Rules=[];settings.ReduceMotion=false;settings.GoalSeconds=14400;settings.AwayThreshold=30;
        engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new(null,null,0));Call("LoadSettingsInputs");Call("Render");await Task.Delay(400);
        window.Left=320;window.Top=260;window.Topmost=true;window.Title="Work Island · 设置保存验收";
        Call("ToggleExpanded",null,new RoutedEventArgs());Call("ShowPage","settings",false);await Task.Delay(300);
        var picker=Field<ComboBox>("GoalPicker");picker.BringIntoView();window.UpdateLayout();window.Activate();Call("Persist");var original=File.ReadAllBytes(store.Path);
        var observations=new List<object>();
        object Snapshot()=>new{goal=settings.GoalSeconds,selection=(picker.SelectedItem as ComboBoxItem)?.Tag?.ToString(),diskGoal=store.Load().Settings.GoalSeconds,message=Field<TextBlock>("MessageLabel").Text,engine.Current.State,page=Field<FrameworkElement>("activePage").Name};
        SelectionChangedEventHandler changed=(_,_)=>window.Dispatcher.BeginInvoke(()=>observations.Add(Snapshot()),System.Windows.Threading.DispatcherPriority.ContextIdle);
        picker.SelectionChanged+=changed;
        var source=(HwndSource)PresentationSource.FromVisual(window);var watch=System.Diagnostics.Stopwatch.StartNew();
        using(var locked=new FileStream(store.Path,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            File.WriteAllText(Path.Combine(output,"input-ready.json"),JsonSerializer.Serialize(new{processId=Environment.ProcessId,windowHandle=source.Handle.ToInt64(),window.Title,scope="Own isolated paused settings window. Primary file is locked after successful preparation. Official click opens goal picker; Home, Down, Return attempts 2 hours. Preparation ends here; observer only reads own controls, model and disk."},AtomicStore.JsonOptions));
            try
            {
                while(watch.Elapsed.TotalSeconds<300&&!File.Exists(Path.Combine(output,"stop-review.txt")))await Task.Delay(100);
                check("official goal edit rolls back model selector and saved value on real IO failure",observations.Count>0&&settings.GoalSeconds==14400&&(picker.SelectedItem as ComboBoxItem)?.Tag?.ToString()=="4"&&store.Load().Settings.GoalSeconds==14400,Snapshot());
                check("official failed edit preserves exact primary bytes",File.ReadAllBytes(store.Path).SequenceEqual(original),null);
                check("actual settings error remains visible without saved success",Field<TextBlock>("MessageLabel").IsVisible&&Field<TextBlock>("MessageLabel").Text.Contains("保存失败")&&!Field<TextBlock>("MessageLabel").Text.Contains("已保存"),Snapshot());
                check("actual failed edit retains settings page and paused state",engine.Current.State=="paused"&&ReferenceEquals(Field<FrameworkElement>("activePage"),Field<FrameworkElement>("SettingsPage")),null);
                var message=Field<TextBlock>("MessageLabel");var viewport=Field<FrameworkElement>("PageScroller");var bounds=message.TransformToAncestor(viewport).TransformBounds(new Rect(message.RenderSize));
                check("actual concise failure feedback fits with rounded-edge breathing room",bounds.Left>=0&&bounds.Top>=0&&bounds.Right<=viewport.ActualWidth+.1&&bounds.Bottom<=viewport.ActualHeight-7.5,new{bounds=bounds.ToString(),viewport.ActualWidth,viewport.ActualHeight});
                File.WriteAllText(Path.Combine(output,"preference-live.json"),JsonSerializer.Serialize(new{productDllSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),processId=Environment.ProcessId,windowHandle=source.Handle.ToInt64(),observations,final=Snapshot(),scope="Official mouse plus keyboard on actual own WPF dropdown under real own-data primary-file lock. Read-only observer after preparation; nested rollback SelectionChanged events are production control synchronization, not extra native actions. Isolated paused data; no user-data, hardware or game acceptance."},AtomicStore.JsonOptions));
            }
            finally{picker.SelectionChanged-=changed;}
        }
    }
}
