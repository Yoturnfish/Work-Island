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
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunStartupFailureLive(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        void Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var engine=Field<TrackingEngine>("engine");var provider=(StartupFailureFixture)Field<IStartupRegistration>("startupRegistration");
        engine.Data.Settings.Paused=true;engine.Data.Settings.Rules=[];engine.Data.Settings.ReduceMotion=false;
        engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new(null,null,0));Call("Render");await Task.Delay(400);
        window.Left=320;window.Top=260;window.Topmost=true;window.Title="Work Island · 登录项验收";
        Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenSettings",window,new RoutedEventArgs());await Task.Delay(300);
        var login=Field<CheckBox>("LoginCheck");login.BringIntoView();window.UpdateLayout();window.Activate();
        var snapshots=new List<object>();var unknown=false;var readableAfterRecovery=false;var recoveredSource=false;var safe=false;var breathingRoom=false;
        object Snapshot(string phase)=>new{phase,provider.Enabled,provider.Reads,provider.Writes,selected=login.IsChecked,editable=login.IsEnabled,opacity=login.Opacity,message=Field<TextBlock>("MessageLabel").Text};
        RoutedEventHandler clicked=(_,_)=>window.Dispatcher.BeginInvoke(()=>
        {
            snapshots.Add(Snapshot("after-official-click"));unknown=!login.IsEnabled&&login.IsChecked is null;
            var message=Field<TextBlock>("MessageLabel");safe=message.Text.Contains("无法读取登录项")&&!message.Text.Contains("private-fixture-path");
            var viewport=Field<FrameworkElement>("PageScroller");var bounds=message.TransformToAncestor(viewport).TransformBounds(new Rect(message.RenderSize));breathingRoom=bounds.Bottom<=viewport.ActualHeight-7.5;
        },DispatcherPriority.ContextIdle);
        login.Click+=clicked;provider.ReadFailureAfterWrite=new IOException("private-fixture-path simulated status read");
        var source=(HwndSource)PresentationSource.FromVisual(window);var watch=System.Diagnostics.Stopwatch.StartNew();
        File.WriteAllText(Path.Combine(output,"input-ready.json"),JsonSerializer.Serialize(new{processId=RuntimeCompat.ProcessId,windowHandle=source.Handle.ToInt64(),window.Title,scope="Isolated startup provider: native click toggles On, write succeeds in fixture, status read throws. File marker removes only provider fault; actual mouse back and Settings reopen must refresh UI. No real registry or GUI handlers after preparation."},AtomicStore.JsonOptions));
        try
        {
            while(watch.Elapsed.TotalSeconds<300&&!File.Exists(Path.Combine(output,"stop-review.txt")))
            {
                if(!recoveredSource&&File.Exists(Path.Combine(output,"recover-login.txt")))
                {
                    provider.ReadFailure=null;provider.ReadFailureAfterWrite=null;recoveredSource=true;snapshots.Add(Snapshot("provider-readable-ui-not-refreshed"));
                    File.WriteAllText(Path.Combine(output,"recovery-ready.json"),"{\"faultRemoved\":true,\"guiAction\":false}");
                }
                if(recoveredSource&&login.IsEnabled&&login.IsChecked==true)readableAfterRecovery=true;
                await Task.Delay(50);
            }
            snapshots.Add(Snapshot("final"));
            check("official login click shows unknown status without losing window",unknown&&window.IsVisible&&provider.Enabled,null);
            check("actual unknown status feedback is safe and inset from lower edge",safe&&breathingRoom,null);
            check("official settings reopen refreshes actual committed On state",recoveredSource&&readableAfterRecovery&&login.IsEnabled&&login.IsChecked==true,null);
            check("refresh never writes a second startup change",provider.Writes==1,new{provider.Reads,provider.Writes});
            check("recovered settings keeps paused state and clears own failure",engine.Current.State=="paused"&&ReferenceEquals(Field<FrameworkElement>("activePage"),Field<FrameworkElement>("SettingsPage"))&&!Field<TextBlock>("MessageLabel").Text.Contains("无法读取登录项"),null);
            File.WriteAllText(Path.Combine(output,"startup-live.json"),JsonSerializer.Serialize(new{productDllSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),processId=RuntimeCompat.ProcessId,windowHandle=source.Handle.ToInt64(),snapshots,scope="Official actual mouse clicks on own paused WPF window. Provider write/read failure and its recovery are injected; observers only read GUI properties and remove provider fault after marker. No actual Windows Run modification, ACL manipulation, real login/session, hardware or game acceptance."},AtomicStore.JsonOptions));
        }
        finally{login.Click-=clicked;}
    }
}
