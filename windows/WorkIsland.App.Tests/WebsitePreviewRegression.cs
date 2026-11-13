using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunWebsitePreviewChecks(IslandWindow window,string output,Action<string,bool,object?> check,bool live)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;var source=Field<RecognitionFixture>("recognitionSource");var store=Field<AtomicStore>("store");
        var saved=JsonSerializer.Serialize(settings,AtomicStore.JsonOptions);var addresses=source.BrowserAddresses;var duplicate=source.DuplicateWebsite;var readOverride=source.BrowserReadOverride;
        var menus=new List<Popup>();
        var legacy=new WebsiteRule("kept.explicit","Kept host","docs.example.com","docs.example.com",false,true,"study");
        var original="https://fixtureuser:fixturepassword@docs.example.com/project/report?fixturetoken=abc#fixturefragment";
        const string originalSafe="https://docs.example.com/project/report",chromeSafe="https://app.example.com/notes",freshSafe="https://mail.changed.test/fresh",chromeFresh="https://docs.changed.test/second";
        void ResetData(){settings.Websites=[legacy];Call("Persist");}
        WebsiteRule[] Added()=>settings.Websites.Where(rule=>rule.Identifier!=legacy.Identifier).ToArray();
        string Label(Button row)=>((DockPanel)row.Content).Children.OfType<TextBlock>().Last().Text;
        string Mark(Button row)=>((DockPanel)row.Content).Children.OfType<TextBlock>().First().Text;
        bool LegacyKept()=>settings.Websites.Any(rule=>rule==legacy);
        async Task Close(Popup popup)
        {
            var child=(FrameworkElement)popup.Child;var closed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var unloaded=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if(!popup.IsOpen&&!child.IsVisible)closed.TrySetResult();if(!child.IsLoaded)unloaded.TrySetResult();
            EventHandler onClosed=(_,_)=>closed.TrySetResult();RoutedEventHandler onUnloaded=(_,_)=>unloaded.TrySetResult();popup.Closed+=onClosed;child.Unloaded+=onUnloaded;
            try{popup.IsOpen=false;var done=Task.WhenAll(closed.Task,unloaded.Task);if(await Task.WhenAny(done,Task.Delay(3000))!=done)throw new TimeoutException("Preview popup teardown incomplete");await done;await Dispatcher.Yield(DispatcherPriority.ContextIdle);}
            finally{popup.Closed-=onClosed;child.Unloaded-=onUnloaded;}
        }
        async Task<Popup> Open()
        {
            await (Task)Call("OpenRecognitionPicker",Field<Button>("AutomaticRecognitionButton"),"all")!;var popup=Field<Popup>("recognitionPicker");menus.Add(popup);window.UpdateLayout();return popup;
        }
        void Capture(Popup popup,string name)
        {
            var visual=(FrameworkElement)popup.Child;visual.UpdateLayout();var bitmap=new RenderTargetBitmap(564,460,192,192,PixelFormats.Pbgra32);bitmap.Render(visual);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(output,name));encoder.Save(stream);
        }
        async Task Settle(Task task){try{await task.WaitAsync(TimeSpan.FromSeconds(6));}catch(Exception)when(task.IsCompleted){}await Dispatcher.Yield(DispatcherPriority.ContextIdle);}
        bool NoSecrets(Popup popup)
        {
            var values=WebsiteDescendants(popup.Child).SelectMany(item=>new[]{item is TextBlock text?text.Text:"",AutomationProperties.GetName(item),AutomationProperties.GetHelpText(item),item is FrameworkElement element?element.ToolTip?.ToString()??"":""});
            return values.All(value=>!new[]{"fixtureuser","fixturepassword","fixturetoken","fixturefragment"}.Any(secret=>value.Contains(secret,StringComparison.Ordinal)));
        }
        try
        {
            settings.Paused=true;settings.ReduceMotion=true;settings.Rules=[];settings.BackgroundStyle="black";source.DuplicateWebsite=true;
            source.BrowserAddresses=new(){{"msedge.exe",original},{"chrome.exe",chromeSafe}};
            window.Title=live?"Work Island · 网页预览验收":"Work Island · 网页预览检查";window.ShowInTaskbar=live;window.Left=340;window.Top=260;
            Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenRecognition",window,new RoutedEventArgs());ResetData();var popup=await Open();
            var edge=WebsiteRow(popup,"msedge.exe","Edge");var chrome=WebsiteRow(popup,"chrome.exe","Chrome");
            check("website row shows Mac safe current-page preview",Label(edge)==originalSafe,Label(edge));
            check("same-keyword browsers retain distinct recognizable page previews",Label(chrome)==chromeSafe&&Label(edge)!=Label(chrome),Label(chrome));
            check("browser accessibility name contains only browser and bounded safe preview",AutomationProperties.GetName(edge)=="Edge · "+originalSafe,AutomationProperties.GetName(edge));
            var label=((DockPanel)edge.Content).Children.OfType<TextBlock>().Last();
            check("website preview uses Mac 10 DIP normal single-line tail trimming",label.FontSize==10&&label.FontWeight==FontWeights.Normal&&label.TextWrapping==TextWrapping.NoWrap&&label.TextTrimming==TextTrimming.CharacterEllipsis,null);
            check("preview label accessibility and tooltip expose no credentials or tokens",NoSecrets(popup),null);
            check("stable website automation ID contains no address",AutomationProperties.GetAutomationId(edge)=="Website-msedge.exe",null);
            Capture(popup,"preview-before-input-2x.png");
            var initialPopupHeight=((FrameworkElement)popup.Child).ActualHeight;var initialRowHeight=edge.ActualHeight;
            source.BrowserAddresses=new(){{"msedge.exe",freshSafe+"?fixturetoken=fresh#fixturefragment"},{"chrome.exe",chromeSafe}};
            if(live)
            {
                var calls=source.BrowserCalls;
                File.WriteAllText(Path.Combine(output,"ready.json"),JsonSerializer.Serialize(new{pid=Environment.ProcessId,window.Title,initialPopupHeight,initialRowHeight,scope="Initial preview points to example; controlled underlying page changed to changed.test before ready. Official mouse only after ready. Not a real browser address read."},AtomicStore.JsonOptions));
                for(var stage=1;stage<=2;stage++)
                {
                    var watch=Stopwatch.StartNew();var marker=Path.Combine(output,$"stage-{stage}.txt");while(!File.Exists(marker)&&watch.Elapsed.TotalSeconds<120)await Task.Delay(100);
                    check($"native stage {stage}: marker arrived",File.Exists(marker),null);var added=stage==1;
                    check($"native stage {stage}: fresh keyword and disk agree",Added().Length==(added?1:0)&&store.Load().Settings.Websites.Count(rule=>rule.Identifier!=legacy.Identifier)==(added?1:0)&&(!added||Added()[0].PathContains=="changed"),null);
                    check($"native stage {stage}: current page was read on each click",source.BrowserCalls==calls+stage,null);
                    check($"native stage {stage}: fresh safe preview and legacy rule retained",Label(edge)==freshSafe&&(added||Label(chrome)==chromeFresh)&&LegacyKept()&&NoSecrets(popup),null);
                    check($"native stage {stage}: popup and recognition page remain",popup.IsOpen&&ReferenceEquals(Field<FrameworkElement>("activePage"),Field<StackPanel>("RecognitionPage")),null);
                    ((FrameworkElement)popup.Child).UpdateLayout();
                    check($"native stage {stage}: focus and capture do not change popup or row height",Math.Abs(((FrameworkElement)popup.Child).ActualHeight-initialPopupHeight)<.01&&Math.Abs(edge.ActualHeight-initialRowHeight)<.01&&Math.Abs(chrome.ActualHeight-initialRowHeight)<.01,null);
                    var text=((DockPanel)(stage==1?edge:chrome).Content).Children.OfType<TextBlock>().Last();var owner=stage==1?edge:chrome;var top=text.TranslatePoint(new Point(),owner).Y;
                    check($"native stage {stage}: focused preview fits its content vertically",top>=owner.Padding.Top&&top+text.ActualHeight<=owner.ActualHeight-owner.Padding.Bottom,null);
                    File.WriteAllText(Path.Combine(output,$"stage-{stage}-observed.json"),JsonSerializer.Serialize(new{added,total=settings.Websites.Count,keywords=Added().Select(rule=>rule.PathContains).ToArray(),edgePreview=Label(edge),chromePreview=Label(chrome),legacyKept=LegacyKept(),calls=source.BrowserCalls,popupHeight=((FrameworkElement)popup.Child).ActualHeight,rowHeight=edge.ActualHeight},AtomicStore.JsonOptions));
                    if(stage==1)source.BrowserAddresses=new(){{"msedge.exe",freshSafe},{"chrome.exe",chromeFresh+"?fixturetoken=next"}};
                }
                return;
            }
            var callsBefore=source.BrowserCalls;await ClickWebsiteRowAsync(window,edge);
            check("click rereads current page instead of using scan-time address",source.BrowserCalls==callsBefore+1,null);
            check("changed page adds current keyword and never stale example",Added().Length==1&&Added()[0].PathContains=="changed"&&store.Load().Settings.Websites.Any(rule=>rule.PathContains=="changed"),null);
            check("capture updates safe visible and accessibility preview in place",Label(edge)==freshSafe&&AutomationProperties.GetName(edge)=="Edge · "+freshSafe,null);
            check("changed-page capture keeps mark tied to current keyword",Mark(edge)=="✓"&&Mark(chrome)=="",null);
            check("capture keeps existing ID range and page",LegacyKept()&&popup.IsOpen&&ReferenceEquals(Field<FrameworkElement>("activePage"),Field<StackPanel>("RecognitionPage")),null);
            Capture(popup,"preview-after-current-capture-2x.png");await ClickWebsiteRowAsync(window,edge);
            check("fresh second click removes current keyword without changing explicit host",Added().Length==0&&LegacyKept(),null);
            ResetData();source.BrowserFailure=new COMException("Synthetic fresh-read failure");await ClickWebsiteRowAsync(window,edge);source.BrowserFailure=null;
            check("failed fresh read cannot toggle the old website",Added().Length==0&&Field<TextBlock>("RecognitionStatus").Text.Contains("暂时无法读取"),null);
            source.BrowserReadOverride=app=>new(app.Identifier,app.DisplayName,app.Window,null,"当前页地址不可读取");await ClickWebsiteRowAsync(window,edge);
            check("unavailable fresh address does not become stale work",Added().Length==0&&Field<TextBlock>("RecognitionStatus").Text.Contains("不可读取"),null);
            source.BrowserReadOverride=null;await ClickWebsiteRowAsync(window,edge);
            check("row can recover after a fresh address becomes available",Added().Length==1&&Added()[0].PathContains=="changed"&&Mark(edge)=="✓",null);
            ResetData();source.BrowserReadOverride=app=>new(app.Identifier,app.DisplayName,(nint)999,freshSafe,null);await ClickWebsiteRowAsync(window,edge);
            check("fresh result for another window cannot change a website rule",Added().Length==0&&Field<TextBlock>("RecognitionStatus").Text=="窗口已变化",null);
            source.BrowserReadOverride=app=>new("firefox.exe",app.DisplayName,app.Window,freshSafe,null);await ClickWebsiteRowAsync(window,edge);
            check("fresh result for another browser cannot change a website rule",Added().Length==0&&Field<TextBlock>("RecognitionStatus").Text=="窗口已变化",null);
            source.BrowserReadOverride=null;source.BrowserDelayMilliseconds=2300;callsBefore=source.BrowserCalls;
            var prior=Field<Task<BrowserPage[]>>("browserDiscovery");edge.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var pending=Field<Task<BrowserPage[]>>("browserDiscovery");
            chrome.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check("rapid captures keep the same serialized provider task",!ReferenceEquals(prior,pending)&&ReferenceEquals(pending,Field<Task<BrowserPage[]>>("browserDiscovery")),null);
            await Settle(pending);source.BrowserDelayMilliseconds=0;
            check("late fresh read is rejected and a second worker was not queued",Added().Length==0&&source.BrowserCalls==callsBefore+1&&Field<TextBlock>("RecognitionStatus").Text.Contains("暂未响应"),null);
            check("capture deadline restores row interaction",edge.IsEnabled&&chrome.IsEnabled,null);
            var longSafe="https://docs.changed.test/"+new string('x',120);source.BrowserAddresses=new(){{"msedge.exe",longSafe+"?fixturetoken=x"},{"chrome.exe",chromeSafe}};
            await ClickWebsiteRowAsync(window,edge);
            check("long fresh preview is bounded before accessibility publication",Label(edge)==longSafe[..52]+"…"&&AutomationProperties.GetName(edge)=="Edge · "+longSafe[..52]+"…"&&NoSecrets(popup),null);
            check("display truncation never becomes part of persisted rule",Added().Length==1&&Added()[0].PathContains=="changed"&&!JsonSerializer.Serialize(store.Load().Settings.Websites).Contains('…'),null);
            ResetData();source.BrowserDelayMilliseconds=300;prior=Field<Task<BrowserPage[]>>("browserDiscovery");edge.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));pending=Field<Task<BrowserPage[]>>("browserDiscovery");await Close(popup);await Settle(pending);source.BrowserDelayMilliseconds=0;
            check("closing picker discards pending capture without mutating data",!popup.IsOpen&&Added().Length==0&&LegacyKept(),null);
            check("close scenario used an actual new capture task",!ReferenceEquals(prior,pending),null);
            popup=await Open();edge=WebsiteRow(popup,"msedge.exe","Edge");source.BrowserDelayMilliseconds=300;edge.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));pending=Field<Task<BrowserPage[]>>("browserDiscovery");Call("CreateProfile");await Settle(pending);source.BrowserDelayMilliseconds=0;
            check("profile switch rejects late capture into the newly active theme",settings.Websites.Count==0&&!popup.IsOpen,null);
        }
        finally
        {
            foreach(var popup in menus)await Close(popup);Call("CloseRecognitionPicker");source.DuplicateWebsite=duplicate;source.BrowserAddresses=addresses;source.BrowserReadOverride=readOverride;source.BrowserFailure=null;source.BrowserDelayMilliseconds=0;
            engine.Data.Settings=JsonSerializer.Deserialize<Settings>(saved,AtomicStore.JsonOptions)!;Call("RebuildRules");Call("CollapseToTimer");
        }
    }
}
