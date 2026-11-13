using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunRecognitionReopenChecks(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        Task Open(string mode)=>(Task)Call("OpenRecognitionPicker",Field<Button>(mode=="all"?"AutomaticRecognitionButton":"AddWebsiteSelectionButton"),mode)!;
        IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {yield return root;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Descendants(VisualTreeHelper.GetChild(root,i)))yield return child;}
        var engine=Field<TrackingEngine>("engine");var saved=JsonSerializer.Serialize(engine.Data.Settings,AtomicStore.JsonOptions);
        var source=Field<RecognitionFixture>("recognitionSource");var traces=new List<object>();
        try
        {
            engine.Data.Settings.Paused=true;engine.Data.Settings.ReduceMotion=false;engine.Data.Settings.BackgroundStyle="black";
            window.Left=320;window.Top=260;window.Topmost=true;
            Call("ToggleExpanded",null,new RoutedEventArgs());Call("ShowPage","recognition",false);await Task.Delay(300);
            foreach(var pair in new[]{("all","all",false),("all","websites",false),("websites","all",false),("all","all",true)})
            {
                source.DelayMilliseconds=source.BrowserDelayMilliseconds=0;await Open(pair.Item1);await Task.Delay(240);
                var old=Field<Popup>("recognitionPicker");var trace=new List<object>();var watch=Stopwatch.StartNew();
                old.Closed+=(_,_)=>trace.Add(new{eventName="old-closed",ms=watch.Elapsed.TotalMilliseconds,scan=Field<int>("recognitionScan")});
                check($"{pair} old popup uses real fade",old.IsOpen&&old.PopupAnimation==PopupAnimation.Fade,null);
                if(pair.Item3)source.BrowserDelayMilliseconds=300;else source.DelayMilliseconds=300;
                Call("CloseRecognitionPicker");trace.Add(new{eventName="close-returned",ms=watch.Elapsed.TotalMilliseconds,scan=Field<int>("recognitionScan")});
                var opening=Open(pair.Item2);var current=Field<Popup>("recognitionPicker");
                var openedSerial=Field<int>("recognitionScan");
                trace.Add(new{eventName="new-open-started",ms=watch.Elapsed.TotalMilliseconds,scan=Field<int>("recognitionScan")});
                await opening;await Task.Delay(220);
                var buttons=Descendants(current.Child).OfType<Button>().Count();
                var texts=Descendants(current.Child).OfType<TextBlock>().Select(t=>t.Text).ToArray();
                trace.Add(new{eventName="new-read-completed",ms=watch.Elapsed.TotalMilliseconds,scan=Field<int>("recognitionScan"),buttons,texts});
                if(pair.Item1=="all"&&pair.Item2=="all")
                {
                    var visual=(FrameworkElement)current.Child;visual.UpdateLayout();
                    var topLeft=visual.PointToScreen(new Point());var bottomRight=visual.PointToScreen(new Point(visual.ActualWidth,visual.ActualHeight));
                    using var bitmap=new System.Drawing.Bitmap((int)Math.Ceiling(bottomRight.X-topLeft.X),(int)Math.Ceiling(bottomRight.Y-topLeft.Y));
                    using(var graphics=System.Drawing.Graphics.FromImage(bitmap))graphics.CopyFromScreen((int)Math.Floor(topLeft.X),(int)Math.Floor(topLeft.Y),0,0,bitmap.Size);
                    var name=(pair.Item3?"browser":"applications")+"-reopened-screen.png";bitmap.Save(Path.Combine(output,name),System.Drawing.Imaging.ImageFormat.Png);
                    trace.Add(new{eventName="actual-popup-screen",name,bitmap.Width,bitmap.Height,dpi=VisualTreeHelper.GetDpi(visual).PixelsPerInchX});
                }
                check($"{pair} new popup stays open on recognition",current.IsOpen&&ReferenceEquals(Field<FrameworkElement>("activePage"),Field<StackPanel>("RecognitionPage")),null);
                check($"{pair} old Closed does not invalidate new scan",Field<int>("recognitionScan")==openedSerial,new{openedSerial,currentSerial=Field<int>("recognitionScan")});
                check($"{pair} new read fills rows after old fade closes",buttons==(pair.Item2=="all"?3:1)&&texts.All(t=>!t.Contains("正在读取")),new{buttons,texts});
                traces.Add(new{from=pair.Item1,to=pair.Item2,delayedPhase=pair.Item3?"browser":"applications",trace});
                source.DelayMilliseconds=source.BrowserDelayMilliseconds=0;Call("CloseRecognitionPicker");await Task.Delay(240);
            }
            source.DelayMilliseconds=300;
            var pending=Open("all");Call("ShowPage","settings",false);await pending;await Task.Delay(200);
            check("leaving recognition still closes picker and rejects late result",!Field<Popup>("recognitionPicker").IsOpen&&ReferenceEquals(Field<FrameworkElement>("activePage"),Field<StackPanel>("SettingsPage")),null);
            source.DelayMilliseconds=0;Call("ShowPage","recognition",false);await Open("all");await Task.Delay(200);
            var last=Field<Popup>("recognitionPicker");var lastSerial=Field<int>("recognitionScan");Call("CloseRecognitionPicker");await Task.Delay(200);
            check("current picker close still invalidates its scan",!last.IsOpen&&Field<int>("recognitionScan")>lastSerial,null);
            engine.Data.Settings.ReduceMotion=true;await Open("all");Call("CloseRecognitionPicker");source.DelayMilliseconds=300;
            await Open("websites");var reduced=Field<Popup>("recognitionPicker");
            check("reduced motion reopen still completes without popup animation",reduced.IsOpen&&reduced.PopupAnimation==PopupAnimation.None&&Descendants(reduced.Child).OfType<Button>().Count()==1,null);
        }
        finally
        {
            source.DelayMilliseconds=source.BrowserDelayMilliseconds=0;Call("CloseRecognitionPicker");await Task.Delay(240);
            engine.Data.Settings=JsonSerializer.Deserialize<Settings>(saved,AtomicStore.JsonOptions)!;
            Call("RebuildRules");Call("Render");Call("CollapseToTimer");
            File.WriteAllText(Path.Combine(output,"recognition-reopen.json"),JsonSerializer.Serialize(new{productDllSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),traces,scope="Actual owned WPF Popup fade, async application and browser discovery with 300ms synthetic source delays, real product Open/Close methods. No external UI input, real browser, daily data or OS setting. Callback ordering observed without synthetic Closed event injection."},AtomicStore.JsonOptions));
        }
    }
}
