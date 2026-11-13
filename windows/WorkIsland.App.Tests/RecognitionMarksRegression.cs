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
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunRecognitionMarksChecks(IslandWindow window,string output,Action<string,bool,object?> check,bool live)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {yield return root;for(var n=0;n<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);n++)foreach(var child in Descendants(System.Windows.Media.VisualTreeHelper.GetChild(root,n)))yield return child;}
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;var source=Field<RecognitionFixture>("recognitionSource");var store=Field<AtomicStore>("store");
        var saved=JsonSerializer.Serialize(settings,AtomicStore.JsonOptions);var records=new List<object>();
        source.DuplicateWebsite=true;settings.Paused=true;settings.ReduceMotion=true;settings.BackgroundStyle="black";settings.Rules=[];settings.Websites=[];
        window.Title=live?"Work Island · 网站标记实测":"Work Island · 网站标记检查";window.ShowInTaskbar=live;window.Left=340;window.Top=260;
        Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenRecognition",null,new RoutedEventArgs());await Task.Delay(250);Field<DispatcherTimer>("focusReturn").Stop();
        async Task<Popup> Open(){await (Task)Call("OpenRecognitionPicker",Field<Button>("AutomaticRecognitionButton"),"all")!;await Task.Delay(100);return Field<Popup>("recognitionPicker");}
        Button Row(Popup popup,string name)=>name.StartsWith("Edge ·",StringComparison.Ordinal)?WebsiteRow(popup,"msedge.exe","Edge"):name.StartsWith("Chrome ·",StringComparison.Ordinal)?WebsiteRow(popup,"chrome.exe","Chrome"):Descendants(popup.Child).OfType<Button>().Single(b=>b.Content is DockPanel d&&d.Children.OfType<TextBlock>().Last().Text==name);
        string Mark(Button button)=>((DockPanel)button.Content).Children.OfType<TextBlock>().First().Text;
        object Snapshot(Popup popup)=>new{websites=settings.Websites.Select(r=>new{r.Identifier,r.IsEnabled}).ToArray(),edge=Mark(Row(popup,"Edge · example")),chrome=Mark(Row(popup,"Chrome · example")),app=Mark(Row(popup,"运行中的软件"))};
        void Record(string phase,Popup popup){records.Add(new{phase,state=Snapshot(popup)});}
        try
        {
            var popup=await Open();var edge=Row(popup,"Edge · example");var chrome=Row(popup,"Chrome · example");var app=Row(popup,"运行中的软件");
            check("two browsers expose separate rows for one normalized website",settings.Websites.Count==0&&Mark(edge)==""&&Mark(chrome)=="",Snapshot(popup));
            if(live)
            {
                File.WriteAllText(Path.Combine(output,"input-ready.json"),JsonSerializer.Serialize(new{processId=Environment.ProcessId,window.Title,scope="Two browser identities and same URL from controlled source. Popup prepared by fixture; only official native mouse input is allowed after this marker. Original product Click handlers mutate independent data."},AtomicStore.JsonOptions));
                for(var stage=1;stage<=2;stage++)
                {
                    var watch=Stopwatch.StartNew();var marker=Path.Combine(output,$"stage-{stage}.txt");
                    while(!File.Exists(marker)&&watch.Elapsed.TotalSeconds<180)await Task.Delay(100);
                    check($"native stage {stage} marker arrived",File.Exists(marker),null);
                    var added=stage==1;
                    check($"native stage {stage} website model and both visible marks agree",settings.Websites.Count==(added?1:0)&&Mark(edge)==(added?"✓":"")&&Mark(chrome)==(added?"✓":""),Snapshot(popup));
                    check($"native stage {stage} keeps popup and recognition page",popup.IsOpen&&ReferenceEquals(Field<FrameworkElement>("activePage"),Field<StackPanel>("RecognitionPage")),null);
                    Record("native-"+stage,popup);
                    File.WriteAllText(Path.Combine(output,$"stage-{stage}-observed.json"),JsonSerializer.Serialize(Snapshot(popup),AtomicStore.JsonOptions));
                }
                return;
            }
            await ClickWebsiteRowAsync(window,edge);Record("edge-add",popup);
            check("adding through Edge marks both duplicate website rows",settings.Websites.Count==1&&Mark(edge)=="✓"&&Mark(chrome)=="✓",Snapshot(popup));
            check("website addition leaves unrelated app mark unchanged",Mark(app)=="",Snapshot(popup));
            await ClickWebsiteRowAsync(window,chrome);Record("chrome-remove",popup);
            check("removing through Chrome clears both duplicate website rows",settings.Websites.Count==0&&Mark(edge)==""&&Mark(chrome)=="",Snapshot(popup));
            app.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check("unrelated application toggle follows its actual rule",settings.Rules.Count==1&&Mark(app)=="✓"&&Mark(edge)==""&&Mark(chrome)=="",Snapshot(popup));
            var bytes=File.ReadAllBytes(store.Path);
            using(var locked=new FileStream(store.Path,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                await ClickWebsiteRowAsync(window,chrome);Record("locked-chrome-add",popup);
                check("failed save still presents both in-memory website marks",settings.Websites.Count==1&&Mark(edge)=="✓"&&Mark(chrome)=="✓",Snapshot(popup));
                check("failed save remains on recognition with explicit pending notice",Field<TextBlock>("RecognitionStatus").Text.Contains("保存失败")&&popup.IsOpen&&ReferenceEquals(Field<FrameworkElement>("activePage"),Field<StackPanel>("RecognitionPage")),Field<TextBlock>("RecognitionStatus").Text);
                check("file lock preserves previous exact persisted bytes",File.ReadAllBytes(store.Path).SequenceEqual(bytes),null);
            }
            Call("Persist");
            check("retry saves one website without duplicate rules",store.Load().Settings.Websites.Count==1&&Mark(edge)=="✓"&&Mark(chrome)=="✓",null);
            Call("CloseRecognitionPicker");await Task.Delay(100);
            popup=await Open();
            check("reopening derives both marks from saved rule",Mark(Row(popup,"Edge · example"))=="✓"&&Mark(Row(popup,"Chrome · example"))=="✓",Snapshot(popup));
            var obsolete=Row(popup,"Edge · example");Call("CloseRecognitionPicker");await Task.Delay(100);popup=await Open();
            obsolete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check("obsolete row cannot mutate a replacement picker rule",settings.Websites.Count==1&&Mark(Row(popup,"Edge · example"))=="✓"&&Mark(Row(popup,"Chrome · example"))=="✓",Snapshot(popup));
            if(settings.Websites.Count==0)await ClickWebsiteRowAsync(window,Row(popup,"Edge · example"));
            Call("CloseRecognitionPicker");await Task.Delay(100);obsolete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check("dismissed row cannot mutate rules after closing",settings.Websites.Count==1,null);
            popup=await Open();obsolete=Row(popup,"Edge · example");Call("CreateProfile");obsolete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check("old profile website row cannot change newly created profile",settings.Websites.Count==0&&settings.Rules.Count==0&&!popup.IsOpen,null);
            check("all changes retain paused state and original historical sessions",settings.Paused&&engine.Data.Sessions.Count==0,null);
        }
        finally
        {
            source.DuplicateWebsite=false;Call("CloseRecognitionPicker");await Task.Delay(100);
            engine.Data.Settings=JsonSerializer.Deserialize<Settings>(saved,AtomicStore.JsonOptions)!;Call("RebuildRules");Call("CollapseToTimer");
            File.WriteAllText(Path.Combine(output,"recognition-marks.json"),JsonSerializer.Serialize(new{live,records,scope="Actual owned WPF Popup and product mutation handlers; two browser identities share one controlled website address. Routine checks raise routed events, live mode waits only for official native input. FileStream save lock is real; browser URLs and identities are controlled, not actual external browsers. Never daily data."},AtomicStore.JsonOptions));
        }
    }
}


