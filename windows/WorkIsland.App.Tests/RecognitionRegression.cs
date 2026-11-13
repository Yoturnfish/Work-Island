using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunRecognitionChecks(IslandWindow window, Action<string,bool,object?> check)
    {
        object? Call(string name, params object?[] args) => typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        T Field<T>(string name) => (T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;
        var saved=System.Text.Json.JsonSerializer.Serialize(settings,AtomicStore.JsonOptions);
        var sessions=engine.Data.Sessions.ToArray();
        var output=Path.GetDirectoryName(Field<AtomicStore>("store").Path)!;
        try
        {
            settings.Rules=[new("recognition-test.exe","Recognition test")];
            Call("ShowPage","recognition",false);await Task.Delay(30);
            var input=Field<TextBox>("RuleInput");input.Text="recognition-test.exe";
            Call("AddRule",null,new RoutedEventArgs());
            check("duplicate app remains on recognition page",ReferenceEquals(Field<FrameworkElement>("activePage"),Field<StackPanel>("RecognitionPage")),null);
            Call("ShowPage","recognition",false);input.Text="";var count=settings.Rules.Count;
            Call("AddRule",null,new RoutedEventArgs());
            check("empty app input neither adds last focus nor leaves recognition",settings.Rules.Count==count && ReferenceEquals(Field<FrameworkElement>("activePage"),Field<StackPanel>("RecognitionPage")),null);
            settings.DisplayTitle="屏幕工作时间";settings.IslandScale=.82;settings.BackgroundStyle="black";settings.ReduceMotion=true;
            Call("ApplyScale");engine.Observe(DateTimeOffset.Now,new("recognition-test.exe","test",0));Call("Render");
            var title=Field<TextBlock>("TitleLabel");var time=Field<TextBlock>("TimeLabel");
            var size=title.FontSize;var weight=title.FontWeight;var opacity=title.Opacity;
            engine.Observe(DateTimeOffset.Now,new("unmatched.exe","test",0));Call("Render");
            check("away title preserves tracking size weight and opacity",title.FontSize==size && title.FontWeight==weight && title.Opacity==opacity,new{title.FontSize,title.Opacity});
            check("timer preserves original Consolas Bold face",time.FontFamily.Source=="Consolas" && time.FontWeight==FontWeights.Bold,new{timer=time.FontFamily.Source,title=title.FontFamily.Source});
            Call("CollapseToTimer");window.UpdateLayout();Call("Render");window.UpdateLayout();
            var titleBounds=title.TranslatePoint(new Point(),window);var timeBounds=time.TranslatePoint(new Point(),window);
            var titleText=new FormattedText("屏幕工作时间",System.Globalization.CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(title.FontFamily,title.FontStyle,title.FontWeight,title.FontStretch),13,Brushes.White,1);
            var timeText=new FormattedText("0H 00M",System.Globalization.CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(time.FontFamily,time.FontStyle,time.FontWeight,time.FontStretch),time.FontSize,Brushes.White,1);
            var titleInk=titleText.BuildGeometry(new Point()).Bounds.Height;var timerInk=timeText.BuildGeometry(new Point()).Bounds.Height;
            check("timer is only slightly larger than original 13 DIP",time.FontSize>13 && time.FontSize<=14 && timerInk<titleInk,new{titleInk,timerInk,time.FontSize});
            var leftInk=titleText.BuildGeometry(new Point()).Bounds;var rightInk=timeText.BuildGeometry(new Point()).Bounds;
            check("timer aligns with visible left title rather than font box",Math.Abs(titleBounds.Y+leftInk.Y+leftInk.Height/2-timeBounds.Y-rightInk.Y-rightInk.Height/2)<.3,new{leftCenter=titleBounds.Y+leftInk.Y+leftInk.Height/2,rightCenter=timeBounds.Y+rightInk.Y+rightInk.Height/2,time.FontSize});
            Call("SaveImage2x",Path.Combine(output,"aligned-away.png"));
            engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new("recognition-test.exe","test",0));Call("Render");window.UpdateLayout();
            Call("SaveImage2x",Path.Combine(output,"aligned-tracking.png"));
            Call("ToggleExpanded",null,new RoutedEventArgs());Call("ShowPage","recognition",false);window.UpdateLayout();
            settings.ReduceMotion=false;
            Call("OpenRecognition",window,new RoutedEventArgs());await Task.Delay(260);
            var applicationHeader=Field<Button>("ApplicationRulesButton");var websiteHeader=Field<Button>("WebsiteRulesButton");
            var applicationArrow=(RotateTransform)Field<System.Windows.Shapes.Path>("ApplicationDisclosure").RenderTransform;
            var websiteArrow=(RotateTransform)Field<System.Windows.Shapes.Path>("WebsiteDisclosure").RenderTransform;
            applicationHeader.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(65);
            check("application disclosure visibly animates on click",applicationArrow.Angle>0 && applicationArrow.Angle<180,new{applicationArrow.Angle});
            await Task.Delay(160);
            check("application arrow and accessibility follow collapsed list",Field<StackPanel>("RuleRows").Visibility==Visibility.Collapsed&&applicationArrow.Angle==0&&System.Windows.Automation.AutomationProperties.GetItemStatus(applicationHeader)=="已收起",null);
            applicationHeader.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(200);
            check("application disclosure returns up when expanded",applicationArrow.Angle==180&&Field<StackPanel>("RuleRows").Visibility==Visibility.Visible,null);
            websiteHeader.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(200);
            check("website disclosure independently tracks collapse",websiteArrow.Angle==0&&applicationArrow.Angle==180&&Field<StackPanel>("WebsiteRows").Visibility==Visibility.Collapsed,null);
            settings.ReduceMotion=true;websiteHeader.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check("reduced motion changes website arrow immediately",websiteArrow.Angle==180&&!websiteArrow.HasAnimatedProperties,null);
            var picker=Field<ApplicationPickerFixture>("applicationPicker");var held=false;picker.OnSelect=()=>held=(bool)Call("HasOpenPicker")!;
            var beforeChoose=settings.Rules.Count;
            Field<Button>("AddApplicationSelectionButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check("application plus opens selection and cancel preserves page",picker.Calls==1&&held&&settings.Rules.Count==beforeChoose&&Field<Grid>("RuleInputRow").Visibility==Visibility.Collapsed&&ReferenceEquals(Field<FrameworkElement>("activePage"),Field<StackPanel>("RecognitionPage")),null);
            picker.NextSelection=Environment.ProcessPath;Field<Button>("AddApplicationSelectionButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var selectedIdentifier=Path.GetFileName(Environment.ProcessPath)!;
            check("application plus adds chosen executable without typed process name",settings.Rules.Any(r=>r.Identifier==selectedIdentifier)&&Field<Grid>("RuleInputRow").Visibility==Visibility.Collapsed,null);
            settings.Rules.RemoveAll(r=>r.Identifier==selectedIdentifier);Call("RebuildRules");
            var candidate=new RunningApplication("fixture-running.exe","运行中的软件",0);
            Call("ToggleRecognizedApplication",candidate);
            check("automatic app add stores actual discovered identity",settings.Rules.Any(r=>r.Identifier==candidate.Identifier),null);
            Call("ToggleRecognizedApplication",candidate);
            check("automatic app second click removes rule without navigation",settings.Rules.All(r=>r.Identifier!=candidate.Identifier)&&ReferenceEquals(Field<FrameworkElement>("activePage"),Field<StackPanel>("RecognitionPage")),null);
            var websiteInput=Field<TextBox>("WebsiteInput");websiteInput.Text="https://example.com/path?token=test";Call("AddWebsite",null,new RoutedEventArgs());
            check("manual website adds safe host and stays on recognition",settings.Websites.Single().DisplayName=="example.com"&&ReferenceEquals(Field<FrameworkElement>("activePage"),Field<StackPanel>("RecognitionPage")),null);
            websiteInput.Text="example.com";Call("AddWebsite",null,new RoutedEventArgs());
            check("duplicate website stays local with one rule",settings.Websites.Count==1&&Field<TextBlock>("RecognitionStatus").IsVisible,null);
            var sites=Field<StackPanel>("WebsiteRows");var grid=(Grid)sites.Children[0];var enabled=grid.Children.OfType<CheckBox>().Single();enabled.IsChecked=false;enabled.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check("website enable checkbox persists production rule",!settings.Websites.Single().IsEnabled,null);
            var page=new BrowserPage("msedge.exe","Edge",0,"https://example.com/",null);Call("ToggleRecognizedWebsite",page);
            check("automatic keyword capture preserves existing explicit disabled host rule",settings.Websites.Count==2&&settings.Websites.Any(r=>r.DisplayName=="example.com"&&!r.IsEnabled)&&settings.Websites[0].DisplayName=="example",null);
            Call("ToggleRecognizedWebsite",page);
            check("automatic keyword second click removes only that keyword",settings.Websites.Count==1&&settings.Websites[0].DisplayName=="example.com"&&!settings.Websites[0].IsEnabled,null);
            grid=(Grid)sites.Children[0];grid.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check("website trash removes only the explicit rule",settings.Websites.Count==0&&settings.Rules.Count==1,null);
            Field<Button>("AddWebsiteSelectionButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(250);
            var websitePicker=Field<Popup>("recognitionPicker");
            check("website plus selects current pages without inline text entry",websitePicker.IsOpen&&ReferenceEquals(websitePicker.PlacementTarget,Field<Button>("AddWebsiteSelectionButton"))&&Field<Grid>("WebsiteInputRow").Visibility==Visibility.Collapsed,null);
            Call("CloseRecognitionPicker");
            Field<Button>("AutomaticRecognitionButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(250);var popup=Field<Popup>("recognitionPicker");
            check("automatic picker opens and holds focus return",popup.IsOpen&&(bool)Call("HasOpenPicker")!,null);
            IEnumerable<DependencyObject> Descendants(DependencyObject root)
            {yield return root;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var item in Descendants(VisualTreeHelper.GetChild(root,i)))yield return item;}
            var fixtureButtons=Descendants(popup.Child).OfType<Button>().ToArray();
            check("picker lists running apps and current browser page",fixtureButtons.Length==3,new{count=fixtureButtons.Length});
            var popupVisual=(FrameworkElement)popup.Child;popupVisual.UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(popupVisual.ActualWidth*2),(int)Math.Ceiling(popupVisual.ActualHeight*2),192,192,PixelFormats.Pbgra32);bitmap.Render(popupVisual);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,"automatic-picker-fixture.png")))encoder.Save(file);
            var appRow=fixtureButtons.Single(b=>((DockPanel)b.Content).Children.OfType<TextBlock>().Any(t=>t.Text=="运行中的软件"));appRow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check("picker app click adds without closing or leaving page",popup.IsOpen&&settings.Rules.Any(r=>r.Identifier=="fixture-running.exe"),null);
            appRow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check("picker checkmark toggles app removal in place",popup.IsOpen&&settings.Rules.All(r=>r.Identifier!="fixture-running.exe"),null);
            Call("ProfileChanged");check("profile switch closes recognition picker",!popup.IsOpen,null);
        }
        finally
        {
            engine.Data.Settings=System.Text.Json.JsonSerializer.Deserialize<Settings>(saved,AtomicStore.JsonOptions)!;
            engine.Data.Sessions.Clear();engine.Data.Sessions.AddRange(sessions);
            engine.ResetObservation();Call("ProfileChanged");Call("ApplyScale");Call("ShowPage","home",false);Call("CollapseToTimer");
            await Task.Delay(300);Field<System.Windows.Threading.DispatcherTimer>("focusReturn").Stop();
        }
    }
}

internal sealed class RecognitionFixture : IRecognitionSource
{
    public int DelayMilliseconds,BrowserDelayMilliseconds,ApplicationCalls,BrowserCalls;
    public bool DuplicateWebsite;
    public string BrowserAddress="https://example.com/path?token=fixture";
    public Dictionary<string,string>? BrowserAddresses;
    public Func<RunningApplication,BrowserPage>? BrowserReadOverride;
    public Exception? ApplicationFailure,BrowserFailure;
    public IReadOnlyList<RunningApplication> Applications()
    {
        System.Threading.Interlocked.Increment(ref ApplicationCalls);
        var failure=ApplicationFailure;
        var delay=DelayMilliseconds;if(delay>0)System.Threading.Thread.Sleep(delay);
        if(failure is not null)throw failure;
        return DuplicateWebsite?[new("fixture-running.exe","运行中的软件",0),new("msedge.exe","Edge",1),new("chrome.exe","Chrome",2)]
            :[new("fixture-running.exe","运行中的软件",0),new("msedge.exe","Edge",1)];
    }
    public BrowserPage CurrentPage(RunningApplication app)
    {
        System.Threading.Interlocked.Increment(ref BrowserCalls);
        var failure=BrowserFailure;
        var delay=BrowserDelayMilliseconds;if(delay>0)System.Threading.Thread.Sleep(delay);
        if(failure is not null)throw failure;
        if(BrowserReadOverride is not null)return BrowserReadOverride(app);
        return new(app.Identifier,app.DisplayName,app.Window,BrowserAddresses is not null&&BrowserAddresses.TryGetValue(app.Identifier,out var address)?address:BrowserAddress,null);
    }
}

internal sealed class ApplicationPickerFixture : IApplicationPicker
{
    public int Calls;public string? NextSelection;public Action? OnSelect;
    public string? Select(Window owner){Calls++;OnSelect?.Invoke();var path=NextSelection;NextSelection=null;return path;}
}
