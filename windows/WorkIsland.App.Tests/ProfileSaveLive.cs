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
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunProfileSaveLive(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        void Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;var store=Field<AtomicStore>("store");settings.Paused=true;settings.Rules=[];settings.ReduceMotion=false;settings.BackgroundStyle="black";
        engine.ResetObservation();Call("Render");await Task.Delay(450);window.Left=320;window.Top=260;window.Topmost=true;window.Title="Work Island · 组合保存验收";
        Call("ToggleExpanded",null,new RoutedEventArgs());Call("ShowPage","recognition",false);await Task.Delay(300);Call("Persist");
        var before=File.ReadAllBytes(store.Path);var original=settings.ActiveProfileId;Call("OpenProfilePopover");
        var menu=Field<Button>("RecognitionColorButton").ContextMenu!;var panel=(StackPanel)((MenuItem)menu.Items[0]).Header;var notice=panel.Children.OfType<TextBlock>().Last();var create=panel.Children.OfType<UniformGrid>().Last().Children.OfType<Button>().First();
        FileStream? held=new(store.Path,FileMode.Open,FileAccess.Read,FileShare.Read);var clicked=false;var failedVisible=false;var bytesPreserved=false;var savedVisible=false;var snapshots=new List<object>();
        object Snapshot(string phase)=>new{phase,settings.ActiveProfileId,settings.DisplayTitle,engine.Current.State,text=notice.Text,menu.IsOpen,diskId=store.Load().Settings.ActiveProfileId};
        RoutedEventHandler click=(_,_)=>window.Dispatcher.BeginInvoke(()=>
        {
            clicked=true;failedVisible=menu.IsOpen&&notice.Text.Contains("保存失败")&&notice.Text.Contains("内存");bytesPreserved=File.ReadAllBytes(store.Path).SequenceEqual(before);snapshots.Add(Snapshot("after-official-mouse"));
        },DispatcherPriority.ContextIdle);
        create.Click+=click;var timer=Field<DispatcherTimer>("timer");timer.Start();
        var source=(HwndSource)PresentationSource.FromVisual(window);var watch=System.Diagnostics.Stopwatch.StartNew();
        File.WriteAllText(Path.Combine(output,"input-ready.json"),JsonSerializer.Serialize(new{processId=RuntimeCompat.ProcessId,windowHandle=source.Handle.ToInt64(),window.Title,scope="Own paused profile popup under actual primary-file lock. Official mouse clicks New via related-popup screenshot ID. Release marker removes only lock; production1Hz timer performs background retry. No GUI handler calls after preparation."},AtomicStore.JsonOptions));
        try
        {
            while(watch.Elapsed.TotalSeconds<300&&!File.Exists(Path.Combine(output,"stop-review.txt")))
            {
                if(held is not null&&File.Exists(Path.Combine(output,"release-profile-lock.txt"))){held.Dispose();held=null;snapshots.Add(Snapshot("lock-released-no-save-call"));}
                if(clicked&&held is null&&notice.Text=="更改已保存。")savedVisible=true;
                await Task.Delay(50);
            }
            snapshots.Add(Snapshot("final"));
            check("official New retains its new in-memory profile under IO failure",clicked&&settings.ActiveProfileId!=original&&settings.Accent=="blue",null);
            check("actual current popup shows safe pending-save feedback",failedVisible,null);
            check("actual failed edit leaves primary bytes intact",bytesPreserved,null);
            check("production background timer saves and refreshes same popup after unlock",savedVisible&&menu.IsOpen&&store.Load().Settings.ActiveProfileId==settings.ActiveProfileId,null);
            check("profile save recovery keeps pause and recognition page",engine.Current.State=="paused"&&ReferenceEquals(Field<FrameworkElement>("activePage"),Field<FrameworkElement>("RecognitionPage")),null);
            File.WriteAllText(Path.Combine(output,"profile-save-live.json"),JsonSerializer.Serialize(new{appSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),coreSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(TrackingEngine).Assembly.Location))),processId=RuntimeCompat.ProcessId,windowHandle=source.Handle.ToInt64(),snapshots,scope="Official mouse clicks existing own profile popup under actual own-data file lock. Marker only releases IO fault; production timer retries without direct Persist/GUI handler calls. Read-only observers; independent paused data, no user profile edits, browser/game/hardware claim."},AtomicStore.JsonOptions));
        }
        finally{timer.Stop();held?.Dispose();create.Click-=click;menu.IsOpen=false;await Task.Delay(250);}
    }
}
