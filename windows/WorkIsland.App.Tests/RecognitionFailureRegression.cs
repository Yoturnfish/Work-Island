using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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
    private static async Task RunRecognitionFailureChecks(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        Task Open()=>(Task)Call("OpenRecognitionPicker",Field<Button>("AutomaticRecognitionButton"),"all")!;
        IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {yield return root;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Descendants(VisualTreeHelper.GetChild(root,i)))yield return child;}
        var engine=Field<TrackingEngine>("engine");var saved=JsonSerializer.Serialize(engine.Data.Settings,AtomicStore.JsonOptions);
        var source=Field<RecognitionFixture>("recognitionSource");var records=new List<object>();
        TextBlock[] Texts()=>Descendants(Field<Popup>("recognitionPicker").Child).OfType<TextBlock>().ToArray();
        int Buttons()=>Descendants(Field<Popup>("recognitionPicker").Child).OfType<Button>().Count();
        async Task Close(){Call("CloseRecognitionPicker");await Task.Delay(80);}
        void Capture(string name)
        {
            var visual=(FrameworkElement)Field<Popup>("recognitionPicker").Child;visual.UpdateLayout();
            var a=visual.PointToScreen(new Point());var b=visual.PointToScreen(new Point(visual.ActualWidth,visual.ActualHeight));
            using var bitmap=new System.Drawing.Bitmap((int)Math.Ceiling(b.X-a.X),(int)Math.Ceiling(b.Y-a.Y));
            using(var graphics=System.Drawing.Graphics.FromImage(bitmap))graphics.CopyFromScreen((int)Math.Floor(a.X),(int)Math.Floor(a.Y),0,0,bitmap.Size);
            bitmap.Save(Path.Combine(output,name+".png"),System.Drawing.Imaging.ImageFormat.Png);
        }
        try
        {
            engine.Data.Settings.Paused=true;engine.Data.Settings.ReduceMotion=true;engine.Data.Settings.BackgroundStyle="black";
            var rules=engine.Data.Settings.Rules.Count;var websites=engine.Data.Settings.Websites.Count;var sessions=engine.Data.Sessions.Count;
            window.Left=320;window.Top=260;window.Topmost=true;
            Call("ToggleExpanded",null,new RoutedEventArgs());Call("ShowPage","recognition",false);await Task.Delay(250);
            foreach(var language in new[]{"zh","en"})foreach(var phase in new[]{"applications","browser"})
            {
                engine.Data.Settings.Language=language;Call("ApplyLanguage");
                if(phase=="applications")source.ApplicationFailure=new IOException("Synthetic application discovery failure");else source.BrowserFailure=new COMException("Synthetic browser discovery failure");
                await Open();await Task.Delay(100);
                var texts=Texts().Select(t=>t.Text).ToArray();var message=language=="zh"?"暂时无法读取，请重新识别。":"Unable to read. Try auto-detect again.";
                check($"{language}/{phase} terminal failure removes loading state",texts.All(t=>!t.Contains("正在读取")&&!t.Contains("Reading current")),texts);
                check($"{language}/{phase} failure is visible inside the active popup",texts.Contains(message),texts);
                check($"{language}/{phase} failure preserves useful apps and recognition page",Buttons()==(phase=="browser"?2:0)&&Field<Popup>("recognitionPicker").IsOpen&&ReferenceEquals(Field<FrameworkElement>("activePage"),Field<StackPanel>("RecognitionPage")),new{buttons=Buttons()});
                if(language=="zh")Capture(phase+"-failure-screen");
                records.Add(new{language,phase,texts,status=Field<TextBlock>("RecognitionStatus").Text});
                source.ApplicationFailure=source.BrowserFailure=null;await Close();await Open();await Task.Delay(100);
                check($"{language}/{phase} retry recovers rows and clears prior failure",Buttons()==3&&Field<TextBlock>("RecognitionStatus").Text==""&&!Texts().Any(t=>t.Text==message),null);
                await Close();
            }
            engine.Data.Settings.Language="en";Call("ApplyLanguage");
            foreach(var phase in new[]{"applications","browser"})
            {
                if(phase=="applications")source.DelayMilliseconds=1200;else source.BrowserDelayMilliseconds=1200;
                var first=Open();await Task.Delay(120);
                await Close();await Open();await Task.Delay(100);
                var notice=Texts().Single(t=>t.Text.Contains("still running"));var root=(FrameworkElement)Field<Popup>("recognitionPicker").Child;root.UpdateLayout();
                var bounds=notice.TransformToAncestor(root).TransformBounds(new Rect(0,0,notice.ActualWidth,notice.ActualHeight));
                var formatted=new FormattedText(notice.Text,CultureInfo.GetCultureInfo("en"),FlowDirection.LeftToRight,new Typeface(notice.FontFamily,notice.FontStyle,notice.FontWeight,notice.FontStretch),notice.FontSize,Brushes.White,VisualTreeHelper.GetDpi(notice).PixelsPerDip){MaxTextWidth=Math.Max(1,notice.ActualWidth)};
                check($"en/{phase} busy notice fits all lines inside popup",bounds.Right<=root.ActualWidth+.5&&notice.ActualHeight>=formatted.Height-1,new{notice.ActualWidth,notice.ActualHeight,requiredHeight=formatted.Height,bounds.Right,popupWidth=root.ActualWidth});
                Capture(phase+"-busy-en-screen");records.Add(new{phase,busyText=notice.Text,notice.ActualWidth,notice.ActualHeight,requiredHeight=formatted.Height});
                source.DelayMilliseconds=source.BrowserDelayMilliseconds=0;await first;await Close();await Open();await Task.Delay(100);
                check($"en/{phase} retry after completed pending read recovers",Buttons()==3,null);await Close();
            }
            foreach(var phase in new[]{"applications","browser"})
            {
                if(phase=="applications"){source.DelayMilliseconds=300;source.ApplicationFailure=new IOException("Late synthetic application failure");}
                else{source.BrowserDelayMilliseconds=300;source.BrowserFailure=new COMException("Late synthetic browser failure");}
                var pending=Open();await Task.Delay(80);Call("ShowPage","settings",false);await pending;await Task.Delay(80);
                check($"{phase} late failure leaves current page and status unchanged",ReferenceEquals(Field<FrameworkElement>("activePage"),Field<StackPanel>("SettingsPage"))&&Field<TextBlock>("RecognitionStatus").Text=="",null);
                check($"{phase} late failure does not reopen dismissed popup",!Field<Popup>("recognitionPicker").IsOpen,null);
                source.ApplicationFailure=source.BrowserFailure=null;source.DelayMilliseconds=source.BrowserDelayMilliseconds=0;
                Call("ShowPage","recognition",false);await Close();
            }
            foreach(var phase in new[]{"applications","browser"})
            {
                if(phase=="applications")source.DelayMilliseconds=1700;else source.BrowserDelayMilliseconds=2200;
                await Open();
                var expected=phase=="applications"?"Application scan timed out. Try again.":"Browser did not respond. Try again.";
                check($"{phase} timeout ends loading with a terminal message",Texts().Any(t=>t.Text==expected)&&!Texts().Any(t=>t.Text.Contains("Reading current")),null);
                await Field<Task>(phase=="applications"?"applicationDiscovery":"browserDiscovery");
                source.DelayMilliseconds=source.BrowserDelayMilliseconds=0;await Close();await Open();await Task.Delay(80);
                check($"{phase} retry after timed-out worker completes recovers",Buttons()==3,null);await Close();
            }
            check("failure and retry never alter rules or history",engine.Data.Settings.Rules.Count==rules&&engine.Data.Settings.Websites.Count==websites&&engine.Data.Sessions.Count==sessions,null);
        }
        finally
        {
            source.ApplicationFailure=source.BrowserFailure=null;source.DelayMilliseconds=source.BrowserDelayMilliseconds=0;
            await Close();engine.Data.Settings=JsonSerializer.Deserialize<Settings>(saved,AtomicStore.JsonOptions)!;
            Call("RebuildRules");Call("ApplyLanguage");Call("Render");Call("CollapseToTimer");
            File.WriteAllText(Path.Combine(output,"recognition-failure.json"),JsonSerializer.Serialize(new{productDllSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),records,scope="Actual owned WPF popup, injected application IOException / browser COMException and bounded 1200ms delays, public source interface. Real layout/full-text metrics and own-popup screen capture; no external UI input, real browser/OS permission or user data."},AtomicStore.JsonOptions));
        }
    }
}
