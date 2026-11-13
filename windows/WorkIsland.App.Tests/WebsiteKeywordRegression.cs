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
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunWebsiteKeywordChecks(IslandWindow window,string output,Action<string,bool,object?> check,bool live)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        IEnumerable<DependencyObject> Descendants(DependencyObject root){yield return root;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Descendants(VisualTreeHelper.GetChild(root,i)))yield return child;}
        var engine=Field<TrackingEngine>("engine");var source=Field<RecognitionFixture>("recognitionSource");var store=Field<AtomicStore>("store");var settings=engine.Data.Settings;
        var saved=JsonSerializer.Serialize(settings,AtomicStore.JsonOptions);var addresses=source.BrowserAddresses;var duplicate=source.DuplicateWebsite;var menus=new List<Popup>();
        var legacy=new WebsiteRule("legacy.explicit","Explicit old host","docs.example.com","docs.example.com",false,true,"study");
        var other=new WebsiteRule("legacy.other","Other rule","other.test","other.test",true,true,"other");
        var legacyText=JsonSerializer.Serialize(new[]{legacy,other},AtomicStore.JsonOptions);
        string Key(WebsiteRule rule)=>(string.IsNullOrEmpty(rule.PathContains)?rule.TitleContains:rule.PathContains).Trim().ToLowerInvariant();
        bool SurvivingLegacy()=>JsonSerializer.Serialize(settings.Websites.Where(r=>r.Identifier.StartsWith("legacy.")).ToArray(),AtomicStore.JsonOptions)==legacyText;
        string Mark(Button row)=>((DockPanel)row.Content).Children.OfType<TextBlock>().First().Text;
        string Label(Button row)=>((DockPanel)row.Content).Children.OfType<TextBlock>().Last().Text;
        Button Row(Popup popup,string browser)=>WebsiteRow(popup,browser=="Edge"?"msedge.exe":"chrome.exe",browser);
        async Task Close(Popup popup)
        {
            var child=(FrameworkElement)popup.Child;var closed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var unloaded=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if(!popup.IsOpen&&!child.IsVisible)closed.TrySetResult();if(!child.IsLoaded)unloaded.TrySetResult();
            EventHandler onClosed=(_,_)=>closed.TrySetResult();RoutedEventHandler onUnloaded=(_,_)=>unloaded.TrySetResult();popup.Closed+=onClosed;child.Unloaded+=onUnloaded;
            try{popup.IsOpen=false;var done=Task.WhenAll(closed.Task,unloaded.Task);if(await Task.WhenAny(done,Task.Delay(3000))!=done)throw new TimeoutException("Keyword popup teardown incomplete");await done;await Dispatcher.Yield(DispatcherPriority.ContextIdle);}
            finally{popup.Closed-=onClosed;child.Unloaded-=onUnloaded;}
        }
        async Task<Popup> Open()
        {
            await (Task)Call("OpenRecognitionPicker",Field<Button>("AutomaticRecognitionButton"),"all")!;
            var popup=Field<Popup>("recognitionPicker");menus.Add(popup);window.UpdateLayout();return popup;
        }
        void Capture(Popup popup,string file)
        {
            var visual=(FrameworkElement)popup.Child;visual.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth*2),(int)Math.Ceiling(visual.ActualHeight*2),192,192,PixelFormats.Pbgra32);bitmap.Render(visual);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(output,file));encoder.Save(stream);
        }
        try
        {
            settings.Paused=true;settings.ReduceMotion=true;settings.Rules=[];settings.Websites=[legacy,other];settings.BackgroundStyle="black";settings.Language="zh";
            source.DuplicateWebsite=true;source.BrowserAddresses=new(){{"msedge.exe","https://docs.example.com/path?q="+new string('x',4096)},{"chrome.exe","https://app.example.com/other"}};
            window.Title=live?"Work Island · 网站关键词验收":"Work Island · 网站关键词检查";window.ShowInTaskbar=live;window.Left=340;window.Top=260;
            Call("ApplyLanguage");Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenRecognition",window,new RoutedEventArgs());Call("Persist");
            var popup=await Open();var edge=Row(popup,"Edge");var chrome=Row(popup,"Chrome");
            check("Edge row identifies current safe subdomain page",Label(edge)=="https://docs.example.com/path",Label(edge));
            check("Chrome row identifies its own current safe page",Label(chrome)=="https://app.example.com/other",Label(chrome));
            Capture(popup,"keyword-before-input-2x.png");
            if(live)
            {
                File.WriteAllText(Path.Combine(output,"ready.json"),JsonSerializer.Serialize(new{pid=RuntimeCompat.ProcessId,window.Title,scope="Two controlled browser addresses from different subdomains; two legacy rules retained. Official mouse input only after ready, not external browser URL acquisition."},AtomicStore.JsonOptions));
                for(var stage=1;stage<=2;stage++)
                {
                    var watch=Stopwatch.StartNew();var marker=Path.Combine(output,$"stage-{stage}.txt");while(!File.Exists(marker)&&watch.Elapsed.TotalSeconds<120)await Task.Delay(100);
                    check($"native stage {stage}: marker arrived",File.Exists(marker),null);var added=stage==1;
                    check($"native stage {stage}: sibling rows and saved keyword agree",settings.Websites.Count(r=>Key(r)=="example")== (added?1:0)&&store.Load().Settings.Websites.Count(r=>Key(r)=="example")== (added?1:0)&&Mark(edge)==(added?"✓":"")&&Mark(chrome)==(added?"✓":""),null);
                    check($"native stage {stage}: surviving old IDs names flags and ranges unchanged",SurvivingLegacy(),null);
                    check($"native stage {stage}: original picker and page retained",popup.IsOpen&&ReferenceEquals(Field<FrameworkElement>("activePage"),Field<StackPanel>("RecognitionPage")),null);
                    File.WriteAllText(Path.Combine(output,$"stage-{stage}-observed.json"),JsonSerializer.Serialize(new{added,total=settings.Websites.Count,keywordCount=settings.Websites.Count(r=>Key(r)=="example"),edge=Mark(edge),chrome=Mark(chrome),legacyUnchanged=SurvivingLegacy()},AtomicStore.JsonOptions));
                }
                return;
            }
            await ClickWebsiteRowAsync(window,edge);
            check("capturing adds one enabled Mac keyword at the front",settings.Websites.Count==3&&Key(settings.Websites[0])=="example"&&settings.Websites[0].IsEnabled&&settings.Websites[0].MatchesHostOnly==true,null);
            check("different subdomains update both website marks in place",Mark(edge)=="✓"&&Mark(chrome)=="✓",null);
            check("capture preserves old host rule IDs names enabled flags categories and ranges",SurvivingLegacy(),null);
            check("capture persists only the keyword and keeps old rules",store.Load().Settings.Websites.Count(r=>Key(r)=="example")==1&&store.Load().Settings.Websites.Count==3,null);
            var addedRule=settings.Websites.FirstOrDefault(r=>Key(r)=="example");
            check("new rule tracks a sibling subdomain and excludes unrelated path text",addedRule is not null&&ActivityDecision.WebsiteMatches("https://mail.example.com/",addedRule)&&!ActivityDecision.WebsiteMatches("https://unrelated.test/example",addedRule),null);
            Capture(popup,"keyword-added-2x.png");await ClickWebsiteRowAsync(window,chrome);
            check("clicking sibling browser removes same keyword instead of adding another host",settings.Websites.Count==2&&!settings.Websites.Any(r=>Key(r)=="example")&&Mark(edge)==""&&Mark(chrome)=="",null);
            check("sibling removal leaves persisted legacy records intact",SurvivingLegacy()&&store.Load().Settings.Websites.Count==2,null);
            await Close(popup);
            settings.Websites=[legacy,other,new("manual.one","Custom name","  EXAMPLE  ","",false,null,"study"),new("manual.two","Second custom","","example",true,true)];Call("Persist");
            popup=await Open();edge=Row(popup,"Edge");chrome=Row(popup,"Chrome");
            check("existing manual keyword with custom IDs marks both rows even when disabled",Mark(edge)=="✓"&&Mark(chrome)=="✓",null);
            Field<TextBox>("WebsiteInput").Text="example";Call("AddWebsite",null,new RoutedEventArgs());
            check("manual addition recognizes custom-ID semantic duplicates without replacing them",settings.Websites.Count==4&&settings.Websites.Any(r=>r.Identifier=="manual.one"&&!r.IsEnabled)&&Field<TextBlock>("RecognitionStatus").Text.Contains("已在列表中"),null);
            await ClickWebsiteRowAsync(window,edge);
            check("toggle removes all same-keyword records using Mac trim case and title fallback",settings.Websites.Count==2&&!settings.Websites.Any(r=>r.Identifier.StartsWith("manual."))&&Mark(edge)==""&&Mark(chrome)=="",null);
            check("semantic duplicate removal does not remove narrower or unrelated legacy rules",SurvivingLegacy(),null);
            var bytes=File.ReadAllBytes(store.Path);
            using(var locked=new FileStream(store.Path,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                await ClickWebsiteRowAsync(window,edge);
                check("failed keyword save keeps both in-memory marks and explicit pending notice",Mark(edge)=="✓"&&Mark(chrome)=="✓"&&Field<TextBlock>("RecognitionStatus").Text.Contains("保存失败"),null);
                check("failed keyword save leaves previous bytes and legacy semantics intact",File.ReadAllBytes(store.Path).SequenceEqual(bytes)&&SurvivingLegacy(),null);
            }
            Call("Persist");check("retry persists one keyword with legacy IDs intact",store.Load().Settings.Websites.Count(r=>Key(r)=="example")==1&&SurvivingLegacy(),null);
            await Close(popup);source.BrowserAddresses=new(){{"msedge.exe","https://127.0.0.1/path"},{"chrome.exe","http://localhost/path"}};
            popup=await Open();
            check("IP and localhost pages do not produce automatic website rows",!Descendants(popup.Child).OfType<Button>().Any(b=>b.Content is DockPanel d&&d.Children.OfType<TextBlock>().Any(x=>x.Text.StartsWith("Edge · ")||x.Text.StartsWith("Chrome · "))),null);
            check("readable unsupported pages explain automatic addition rather than failed URL read",Descendants(popup.Child).OfType<TextBlock>().Count(x=>x.Text.Contains("此网页暂不支持自动添加"))==2,null);
            await Close(popup);settings.Language="en";Call("ApplyLanguage");popup=await Open();
            check("unsupported automatic-page feedback follows English language",Descendants(popup.Child).OfType<TextBlock>().Count(x=>x.Text.Contains("This page cannot be added automatically"))==2,null);
        }
        finally
        {
            foreach(var popup in menus)await Close(popup);Call("CloseRecognitionPicker");source.DuplicateWebsite=duplicate;source.BrowserAddresses=addresses;
            engine.Data.Settings=JsonSerializer.Deserialize<Settings>(saved,AtomicStore.JsonOptions)!;Call("RebuildRules");Call("ApplyLanguage");Call("CollapseToTimer");
        }
    }
}

