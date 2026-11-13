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
using System.Windows.Input;
using System.Windows.Interop;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunPauseResetLive(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        void Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;settings.Paused=true;settings.Rules=[];settings.ReduceMotion=false;settings.BackgroundStyle="black";
        engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new(null,null,0));Call("Render");await Task.Delay(450);
        window.Left=320;window.Top=260;window.Topmost=true;window.Title="Work Island · 暂停组合验收";
        Call("ToggleExpanded",null,new RoutedEventArgs());Call("ShowPage","recognition",false);await Task.Delay(300);Call("OpenProfilePopover");
        var menu=Field<Button>("RecognitionColorButton").ContextMenu!;var panel=(StackPanel)((MenuItem)menu.Items[0]).Header;
        var create=panel.Children.OfType<UniformGrid>().Last().Children.OfType<Button>().First();
        window.Activate();create.Focus();Keyboard.Focus(create);await Task.Delay(100);
        var original=settings.ActiveProfileId;var clicked=false;var allPaused=true;var samples=new List<object>();var clickAt=(double?)null;var watch=System.Diagnostics.Stopwatch.StartNew();
        RoutedEventHandler click=(_,_)=>{clicked=true;clickAt=watch.Elapsed.TotalSeconds;};create.Click+=click;
        var source=(HwndSource)PresentationSource.FromVisual(window);
        File.WriteAllText(Path.Combine(output,"input-ready.json"),JsonSerializer.Serialize(new{processId=Environment.ProcessId,windowHandle=source.Handle.ToInt64(),window.Title,scope="Own isolated paused recognition/profile popover. Fixture prepares focus on the real New profile button. Official input must invoke it. No generated input or product handlers after preparation; timer stopped for bounded observation."},AtomicStore.JsonOptions));
        try
        {
            while(watch.Elapsed.TotalSeconds<300&&!File.Exists(Path.Combine(output,"stop-review.txt")))
            {
                if(clickAt is {} start&&watch.Elapsed.TotalSeconds-start<1.3)
                {
                    var state=engine.Current.State;var visual=Field<string>("visualState");allPaused&=settings.Paused&&state=="paused"&&visual=="paused";
                    samples.Add(new{seconds=watch.Elapsed.TotalSeconds-start,state,visual,settings.Paused,settings.ActiveProfileId});
                }
                await Task.Delay(16);
            }
            check("official input creates a real new independent profile",clicked&&settings.ActiveProfileId!=original&&ColorProfilesService.Active(settings) is {Accent:"blue",Title:"新组合"},null);
            check("native profile command preserves manual pause throughout observed interval",samples.Count>10&&allPaused&&settings.Paused&&engine.Current.State=="paused",new{samples=samples.Count});
            check("new profile command cannot make a false away presentation",Field<string>("visualState")=="paused"&&Field<object?>("stateTransition")==null,null);
            File.WriteAllText(Path.Combine(output,"pause-reset-live.json"),JsonSerializer.Serialize(new{appSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),coreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(TrackingEngine).Assembly.Location))),processId=Environment.ProcessId,windowHandle=source.Handle.ToInt64(),clicked,samples,scope="Official input on the existing own profile popover New button, read-only logical/presentation observers. Prepared paused isolated state and stopped timer; compact icons are hidden by recognition page, not visible compositor-frame evidence. No real lock/sleep/time change, user data, hardware or game acceptance."},AtomicStore.JsonOptions));
        }
        finally{create.Click-=click;if(menu.IsOpen)menu.IsOpen=false;}
    }
}

