using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunWebsiteAddressChecks(IslandWindow window,string output,Action<string,bool,object?> check,bool live)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        IEnumerable<DependencyObject> Descendants(DependencyObject root){yield return root;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Descendants(VisualTreeHelper.GetChild(root,i)))yield return child;}
        async Task Close(Popup popup)
        {
            var child=(FrameworkElement)popup.Child;
            var closed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var unloaded=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if(!popup.IsOpen&&!child.IsVisible)closed.TrySetResult();if(!child.IsLoaded)unloaded.TrySetResult();
            EventHandler onClosed=(_,_)=>closed.TrySetResult();RoutedEventHandler onUnloaded=(_,_)=>unloaded.TrySetResult();
            popup.Closed+=onClosed;child.Unloaded+=onUnloaded;
            try{popup.IsOpen=false;var done=Task.WhenAll(closed.Task,unloaded.Task);if(await Task.WhenAny(done,Task.Delay(3000))!=done)throw new TimeoutException("Owned website popup teardown not complete");await done;await Dispatcher.Yield(DispatcherPriority.ContextIdle);}
            finally{popup.Closed-=onClosed;child.Unloaded-=onUnloaded;}
        }
        var engine=Field<TrackingEngine>("engine");var source=Field<RecognitionFixture>("recognitionSource");var store=Field<AtomicStore>("store");
        var saved=JsonSerializer.Serialize(engine.Data.Settings,AtomicStore.JsonOptions);var address=source.BrowserAddress;var menus=new List<Popup>();
        try
        {
            var settings=engine.Data.Settings;settings.Paused=true;settings.ReduceMotion=true;settings.Rules=[];settings.Websites=[];settings.BackgroundStyle="black";
            source.BrowserAddress="https://docs.example.com/search?fixturetoken="+new string('x',4096)+"#fixturefragment";
            window.Title=live?"Work Island · 长网页识别验收":"Work Island · 长网页识别检查";window.ShowInTaskbar=live;window.Left=340;window.Top=260;
            Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenRecognition",window,new RoutedEventArgs());
            foreach(var mode in new[]{"all","websites"})
            {
                await (Task)Call("OpenRecognitionPicker",Field<Button>(mode=="all"?"AutomaticRecognitionButton":"AddWebsiteSelectionButton"),mode)!;
                var popup=Field<Popup>("recognitionPicker");menus.Add(popup);window.UpdateLayout();
                var rows=Descendants(popup.Child).OfType<Button>().ToArray();
                var row=rows.SingleOrDefault(b=>b.Content is DockPanel line&&System.Windows.Automation.AutomationProperties.GetAutomationId(b)=="Website-msedge.exe");
                check(mode+": long current page produces a selectable website row",row is not null,new{sourceLength=source.BrowserAddress.Length,rows=rows.Length});
                check(mode+": popup keeps recognition page and uses no inline URL entry",popup.IsOpen&&ReferenceEquals(Field<FrameworkElement>("activePage"),Field<StackPanel>("RecognitionPage"))&&Field<Grid>("WebsiteInputRow").Visibility==Visibility.Collapsed,null);
                check(mode+": no credential query fragment in visible row labels",Descendants(popup.Child).OfType<TextBlock>().All(x=>!x.Text.Contains("fixturetoken")&&!x.Text.Contains("fixturefragment")&&!x.Text.Contains(new string('x',30))),null);
                if(row is not null)
                {
                    string Mark()=>((DockPanel)row.Content).Children.OfType<TextBlock>().First().Text;
                    if(live)
                    {
                        File.WriteAllText(Path.Combine(output,"ready.json"),JsonSerializer.Serialize(new{pid=RuntimeCompat.ProcessId,window.Title,addressLength=source.BrowserAddress.Length,scope="Controlled browser source and real product WPF handlers; official native input only after ready. Not an external browser URL read."},AtomicStore.JsonOptions));
                        for(var stage=1;stage<=2;stage++)
                        {
                            var watch=Stopwatch.StartNew();var marker=Path.Combine(output,$"stage-{stage}.txt");while(!File.Exists(marker)&&watch.Elapsed.TotalSeconds<120)await Task.Delay(100);
                            check($"native stage {stage}: marker arrived",File.Exists(marker),null);
                            var added=stage==1;
                            check($"native stage {stage}: model saved rule and mark agree",settings.Websites.Count==(added?1:0)&&store.Load().Settings.Websites.Count==(added?1:0)&&Mark()==(added?"✓":""),null);
                            check($"native stage {stage}: popup and recognition page remain open",popup.IsOpen&&ReferenceEquals(Field<FrameworkElement>("activePage"),Field<StackPanel>("RecognitionPage")),null);
                            File.WriteAllText(Path.Combine(output,$"stage-{stage}-observed.json"),JsonSerializer.Serialize(new{added,count=settings.Websites.Count,mark=Mark()},AtomicStore.JsonOptions));
                        }
                        return;
                    }
                    await ClickWebsiteRowAsync(window,row);
                    check(mode+": website addition persists host-only rule and checkmark",settings.Websites.SingleOrDefault() is {DisplayName:"example",PathContains:"example",MatchesHostOnly:true}&&store.Load().Settings.Websites.Count==1&&Mark()=="✓",null);
                    check(mode+": saved rule contains no long URL query or fragment",!JsonSerializer.Serialize(store.Load().Settings.Websites).Contains("fixturetoken"),null);
                    var decision=ActivityDecision.Evaluate(new("msedge.exe","Edge",0,BrowserURL:source.BrowserAddress),[],settings.Websites,RecognitionRules.Browsers,30,false);
                    check(mode+": captured website is recognized as current work",decision.Target?.Source=="website",decision.State);
                    await ClickWebsiteRowAsync(window,row);
                    check(mode+": second click removes rule and clears mark without leaving picker",settings.Websites.Count==0&&store.Load().Settings.Websites.Count==0&&Mark()==""&&popup.IsOpen,null);
                }
                await Close(popup);
            }
        }
        finally
        {
            source.BrowserAddress=address;foreach(var menu in menus)await Close(menu);Call("CloseRecognitionPicker");
            engine.Data.Settings=JsonSerializer.Deserialize<Settings>(saved,AtomicStore.JsonOptions)!;Call("RebuildRules");Call("CollapseToTimer");
        }
    }
}


