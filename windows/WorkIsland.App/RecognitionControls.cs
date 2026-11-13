using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Automation;
using System.Windows.Documents;
using System.IO;
using System.Diagnostics;
using WorkIsland.Core;

namespace WorkIsland.App;

public partial class IslandWindow
{
    private Popup? recognitionPicker;
    private int recognitionScan;
    private Task<BrowserPage[]>? browserDiscovery;
    private Task<IReadOnlyList<RunningApplication>>? applicationDiscovery;
    private bool applicationChooserOpen;
    private FrameworkElement? dismissedRecognitionAnchor;
    private long recognitionDismissedAt;
    private string recognitionMessageKey="";
    private const string RecognitionSaveFailure="保存失败，更改暂留内存。请检查存储位置。";
    private bool recognitionSavePending;
    private bool PersistRecognitionChange()
    {
        if(Persist())return true;
        recognitionSavePending=true;
        // An explicit edit must report failure even when the background saver
        // has already reported the same storage error and suppresses repeats.
        RecognitionMessage(RecognitionSaveFailure);return false;
    }
    private void ResolveRecognitionSaveFeedback()
    {
        if(!recognitionSavePending)return;
        recognitionSavePending=false;
        if(recognitionMessageKey==RecognitionSaveFailure)RecognitionMessage("更改已保存。");
    }
    private void RecognitionPickerKey(object sender,KeyEventArgs e)
    {
        dismissedRecognitionAnchor=null;
        if(e.Key!=Key.Escape || recognitionPicker?.IsOpen!=true)return;
        var anchor=recognitionPicker.PlacementTarget as FrameworkElement;
        CloseRecognitionPicker();e.Handled=true;anchor?.Focus();
    }
    private void RefreshRecognitionHeaders(bool animate=false)
    {
        foreach(var (button,arrow,rows,name) in new[]{(ApplicationRulesButton,ApplicationDisclosure,RuleRows,"应用程序"),(WebsiteRulesButton,WebsiteDisclosure,WebsiteRows,"网站")})
        {
            var expanded=rows.Visibility==Visibility.Visible;
            var rotation=(RotateTransform)arrow.RenderTransform;var current=rotation.Angle;var target=expanded?180:0;
            rotation.BeginAnimation(RotateTransform.AngleProperty,null);rotation.Angle=target;
            if(animate && !engine.Data.Settings.ReduceMotion && SystemParameters.ClientAreaAnimation)
                rotation.BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(current,target,TimeSpan.FromSeconds(.18)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseInOut},FillBehavior=FillBehavior.Stop});
            arrow.Stroke=expanded?new SolidColorBrush(ThemeColor):new SolidColorBrush(Color.FromArgb(173,255,255,255));
            button.Background=new SolidColorBrush(Color.FromArgb(expanded?(byte)36:(byte)20,255,255,255));
            AutomationProperties.SetName(button,engine.Data.Settings.Language=="en"?(expanded?"Collapse ":"Expand ")+Text(name):(expanded?"收起":"展开")+name);
            AutomationProperties.SetItemStatus(button,engine.Data.Settings.Language=="en"?(expanded?"Expanded":"Collapsed"):(expanded?"已展开":"已收起"));
        }
    }
    private void ChooseApplication(object sender,RoutedEventArgs e)
    {
        if(!CanOpenRecognitionSurface())return;
        CloseRecognitionPicker();focusReturn.Stop();applicationChooserOpen=true;
        try
        {
            var path=applicationPicker.Select(this);
            if(path is null)return;
            if(!File.Exists(path) || !Path.GetExtension(path).Equals(".exe",StringComparison.OrdinalIgnoreCase))
            {RecognitionMessage("请选择有效的应用程序（.exe）。");return;}
            var identifier=Path.GetFileName(path);
            if(engine.Data.Settings.Rules.Any(r=>r.Identifier.Equals(identifier,StringComparison.OrdinalIgnoreCase)))
            {RuleRows.Visibility=Visibility.Visible;RefreshRecognitionHeaders();RecognitionMessage("这个应用已在列表中。勾选它即可启用。");return;}
            var name=FileVersionInfo.GetVersionInfo(path).FileDescription;
            engine.Data.Settings.Rules.Add(new(identifier,string.IsNullOrWhiteSpace(name)?Path.GetFileNameWithoutExtension(path):name));
            RuleRows.Visibility=Visibility.Visible;var saved=PersistRecognitionChange();RebuildRules();if(saved)RecognitionMessage("已添加 "+identifier);
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {RecognitionMessage("未能添加应用，请重新选择。");}
        finally{applicationChooserOpen=false;if(!IsActive&&!smoke)BeginFocusReturn();}
    }
    private void RecognitionMessage(string message)
    {
        recognitionMessageKey=message;
        foreach(var prefix in new[]{"已添加","已移除"})if(message.StartsWith(prefix+" ",StringComparison.Ordinal)){message=Text(prefix)+message[prefix.Length..];break;}
        message=Text(message);
        if(OnboardingActive){OnboardingMessage(message);return;} RecognitionStatus.Text=message;RecognitionStatus.Visibility=string.IsNullOrEmpty(message)?Visibility.Collapsed:Visibility.Visible;
    }
    private TextBlock RuleName(string name)=>new(){Text=name,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};
    private void CloseRecognitionPicker(){recognitionScan++;if(recognitionPicker is not null)recognitionPicker.IsOpen=false;}
    private void ToggleWebsiteRules(object sender,RoutedEventArgs e){WebsiteRows.Visibility=WebsiteRows.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;RefreshRecognitionHeaders(true);}
    private async void ChooseWebsite(object sender,RoutedEventArgs e)=>await OpenRecognitionPicker(AddWebsiteSelectionButton,"websites");
    private void RuleInputKey(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Enter){if(ReferenceEquals(sender,WebsiteInput))AddWebsite(sender,e);else AddRule(sender,e);e.Handled=true;}
        else if(e.Key==Key.Escape){RuleInputRow.Visibility=WebsiteInputRow.Visibility=Visibility.Collapsed;e.Handled=true;}
    }
    private void AddWebsite(object? sender,RoutedEventArgs e)
    {
        var rule=RecognitionRules.Website(WebsiteInput.Text);
        if(rule is null){RecognitionMessage("请输入网站域名或关键词，例如 github.com。");return;}
        if(engine.Data.Settings.Websites.Any(r=>RecognitionRules.SameWebsiteKeyword(r,rule.PathContains))){RecognitionMessage("这个网站已在列表中。勾选它即可启用。");return;}
        engine.Data.Settings.Websites.Add(rule);WebsiteInput.Clear();WebsiteRows.Visibility=Visibility.Visible;
        var saved=PersistRecognitionChange();RebuildRules();if(saved)RecognitionMessage("已添加 "+rule.DisplayName);
    }
    private void RebuildWebsites()
    {
        WebsiteRows.Children.Clear();
        foreach(var rule in engine.Data.Settings.Websites.ToArray())
        {
            var row=new Grid{Margin=new Thickness(0,3,0,3)};
            row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var check=new CheckBox{Content=RuleName(rule.DisplayName),IsChecked=rule.IsEnabled,FontSize=12,Style=(Style)FindResource("RuleCheckStyle")};
            check.Click+=(_,_)=>{var index=engine.Data.Settings.Websites.IndexOf(rule);if(index>=0){engine.Data.Settings.Websites[index]=rule with{IsEnabled=check.IsChecked==true};PersistRecognitionChange();RebuildRules();}};
            var remove=new Button{Content=VectorIcon.Make("trash",new SolidColorBrush(Color.FromArgb(173,255,255,255)),12),Width=18,Height=24,Margin=new Thickness(6,0,0,0)};
            remove.Click+=(_,_)=>{engine.Data.Settings.Websites.Remove(rule);PersistRecognitionChange();RebuildRules();};
            Grid.SetColumn(remove,1);row.Children.Add(check);row.Children.Add(remove);WebsiteRows.Children.Add(row);
        }
    }
    private void ToggleRecognizedApplication(RunningApplication app)
    {
        var rules=engine.Data.Settings.Rules;
        var existing=rules.FirstOrDefault(r=>r.Identifier.Equals(app.Identifier,StringComparison.OrdinalIgnoreCase));
        if(existing is not null)rules.Remove(existing);else rules.Add(new(app.Identifier,app.DisplayName));
        RuleRows.Visibility=Visibility.Visible;var saved=PersistRecognitionChange();RebuildRules();if(saved)RecognitionMessage(existing is null?"已添加 "+app.DisplayName:"已移除 "+app.DisplayName);
    }
    private void ToggleRecognizedWebsite(BrowserPage page)
    {
        var rule=RecognitionRules.AutomaticWebsite(page.Address);
        if(rule is null){RecognitionMessage(UnavailableWebsiteMessage(page));return;}
        var rules=engine.Data.Settings.Websites;var existing=rules.Any(r=>RecognitionRules.SameWebsiteKeyword(r,rule.PathContains));
        if(existing)rules.RemoveAll(r=>RecognitionRules.SameWebsiteKeyword(r,rule.PathContains));else rules.Insert(0,rule);
        WebsiteRows.Visibility=Visibility.Visible;var saved=PersistRecognitionChange();RebuildRules();if(saved)RecognitionMessage(existing?"已移除 "+rule.DisplayName:"已添加 "+rule.DisplayName);
    }
    private static string UnavailableWebsiteMessage(BrowserPage page)=>page.Unavailable??(string.IsNullOrWhiteSpace(page.Address)?"当前页地址不可读取":"此网页暂不支持自动添加");
    private async void RecognizeCurrentApplication(object sender,RoutedEventArgs e)=>await OpenRecognitionPicker(AutomaticRecognitionButton,"all");
    private bool CanOpenRecognitionSurface()
    {
        // A queued click can arrive between fullscreen polling ticks or after
        // its owner was hidden. Check at creation, as the tray menu already does.
        if(!smoke)RefreshFullscreenAvoidance();
        return !exiting&&!suspended&&!startupSuppressed&&!fullscreenSuppressed&&IsVisible;
    }
    private async Task OpenRecognitionPicker(FrameworkElement anchor,string mode)
    {
        if(!CanOpenRecognitionSurface())return;
        // Popup auto-dismiss happens on mouse-down, before the anchor's Click.
        // Consume that same click instead of reopening the just-closed picker.
        if(dismissedRecognitionAnchor==anchor && (Stopwatch.GetTimestamp()-recognitionDismissedAt)/(double)Stopwatch.Frequency<1.5)
        {dismissedRecognitionAnchor=null;return;}
        dismissedRecognitionAnchor=null;
        if(recognitionPicker?.IsOpen==true && recognitionPicker.PlacementTarget==anchor){CloseRecognitionPicker();return;}
        CloseRecognitionPicker();
        focusReturn.Stop();RecognitionMessage("");var serial=++recognitionScan;
        var panel=new StackPanel{Margin=new Thickness(10)};
        var border=new Border{Focusable=true,Background=new SolidColorBrush(Color.FromRgb(31,35,32)),BorderBrush=new SolidColorBrush(Color.FromArgb(60,255,255,255)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Child=new ScrollViewer{Content=panel,MaxHeight=260,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled}};
        TextElement.SetFontFamily(border,FontFamily);KeyboardNavigation.SetTabNavigation(border,KeyboardNavigationMode.Cycle);
        var popup=new Popup{PlacementTarget=anchor,Placement=PlacementMode.Bottom,AllowsTransparency=true,StaysOpen=false,Width=282,Child=border};
        popup.PopupAnimation=engine.Data.Settings.ReduceMotion||!SystemParameters.ClientAreaAnimation?PopupAnimation.None:PopupAnimation.Fade;
        popup.Resources[typeof(ScrollBar)]=FindResource(typeof(ScrollBar));
        recognitionPicker=popup;popup.Closed+=(_,_)=>
        {
            // Fade-out raises Closed after a replacement picker may have opened.
            // An obsolete popup must not invalidate that picker's scan or focus.
            if(!ReferenceEquals(recognitionPicker,popup))return;
            recognitionScan++;
            var point=Mouse.GetPosition(anchor);
            if(Mouse.LeftButton==MouseButtonState.Pressed && point.X>=0&&point.Y>=0&&point.X<anchor.ActualWidth&&point.Y<anchor.ActualHeight)
            {dismissedRecognitionAnchor=anchor;recognitionDismissedAt=Stopwatch.GetTimestamp();}
            if(!IsActive&&!smoke)BeginFocusReturn();
        };
        border.PreviewKeyDown+=RecognitionPickerKey;
        popup.Opened+=(_,_)=>{if(popup.IsOpen){border.Focus();Keyboard.Focus(border);}};
        var sites=new StackPanel();var appsPanel=new StackPanel();
        void Notice(string message)
        {
            sites.Children.Clear();
            sites.Children.Add(new TextBlock{Text=Text(message),Foreground=Brushes.Gray,FontSize=12,TextWrapping=TextWrapping.Wrap});
        }
        void Header(string text)=>panel.Children.Add(new TextBlock{Text=text,Foreground=new SolidColorBrush(Color.FromArgb(173,255,255,255)),FontSize=11,Margin=new Thickness(3,5,0,3)});
        Header(Text(mode=="websites"?"选择当前网页":"当前网页"));panel.Children.Add(sites);
        if(mode=="all"){Header(Text("当前应用程序"));panel.Children.Add(appsPanel);}
        sites.Children.Add(new TextBlock{Text=Text("正在读取浏览器当前页…"),FontSize=12,Foreground=Brushes.Gray,Margin=new Thickness(3)});
        popup.IsOpen=true;
        try
        {
            if(applicationDiscovery is {IsCompleted:false})
            {Notice("上一次应用读取尚未结束，请稍后重试");return;}
            applicationDiscovery=Task.Run(recognitionSource.Applications);
            var applications=applicationDiscovery;
            var applicationResult=await Task.WhenAny(applications,Task.Delay(1500));
            if(serial!=recognitionScan || !popup.IsOpen)return;
            if(applicationResult!=applications)
            {Notice("应用列表读取超时，请稍后重试");return;}
            var apps=await applications;
            if(serial!=recognitionScan || !popup.IsOpen)return;
            var refreshMarks=new List<Action>();
            var websiteButtons=new List<Button>();
            bool Current()=>serial==recognitionScan&&popup.IsOpen&&ReferenceEquals(recognitionPicker,popup);
            Button Row(string name,Func<bool> selected,Func<Task> click,TextBlock? label=null,bool website=false,string? accessibilityName=null,string? automationId=null)
            {
                var line=new DockPanel();var mark=new TextBlock{Text=selected()?"✓":"",Width=19,Foreground=new SolidColorBrush(ThemeColor),VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(mark,Dock.Right);line.Children.Add(mark);line.Children.Add(label??RuleName(name));
                // Reserve room for the keyboard-focus outline so it cannot
                // change the website row height when a capture receives focus.
                var button=new Button{Content=line,Style=(Style)FindResource(typeof(Button)),Height=website?28:27,MinHeight=27,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=website?new Thickness(7,5,7,5):new Thickness(4,2,4,2),Foreground=Brushes.White,FontSize=website?10:12};
                if(accessibilityName is not null)AutomationProperties.SetName(button,accessibilityName);
                if(automationId is not null)AutomationProperties.SetAutomationId(button,automationId);
                refreshMarks.Add(()=>mark.Text=selected()?"✓":"");
                button.Click+=async (_,_)=>
                {
                    if(!Current()||!CanOpenRecognitionSurface())return;
                    await click();
                    // Several browsers may show the same host. All rows reflect
                    // the rule that changed, including pending in-memory saves.
                    if(Current())foreach(var refresh in refreshMarks)refresh();
                };return button;
            }
            async Task CaptureWebsite(BrowserPage page,Action<BrowserPage> received)
            {
                if(browserDiscovery is {IsCompleted:false}){RecognitionMessage("上一次读取尚未结束，请稍后重试");return;}
                foreach(var button in websiteButtons)button.IsEnabled=false;
                RecognitionMessage("正在读取浏览器当前页…");var requestedAt=Stopwatch.GetTimestamp();
                try
                {
                    // Reuse the scan slot. An unresponsive provider keeps that
                    // one slot until terminal; repeated clicks cannot queue work.
                    browserDiscovery=Task.Run(()=>new[]{recognitionSource.CurrentPage(new(page.BrowserIdentifier,page.BrowserName,page.Window))});
                    var reading=browserDiscovery;var finished=await Task.WhenAny(reading,Task.Delay(2000));
                    if(!Current()||!CanOpenRecognitionSurface())return;
                    if(finished!=reading||(Stopwatch.GetTimestamp()-requestedAt)/(double)Stopwatch.Frequency>2)
                    {RecognitionMessage("浏览器暂未响应，请稍后重新识别");return;}
                    var fresh=(await reading)[0];
                    if(fresh.Window!=page.Window||!fresh.BrowserIdentifier.Equals(page.BrowserIdentifier,StringComparison.OrdinalIgnoreCase))
                    {RecognitionMessage("窗口已变化");return;}
                    received(fresh);
                    if(RecognitionRules.AutomaticWebsite(fresh.Address) is null){RecognitionMessage(UnavailableWebsiteMessage(fresh));return;}
                    ToggleRecognizedWebsite(fresh);
                }
                catch(Exception error)when(error is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException or ArgumentException or TimeoutException)
                {if(Current()){RecognitionMessage("暂时无法读取，请重新识别。");}}
                finally{if(Current())foreach(var button in websiteButtons)button.IsEnabled=true;}
            }
            if(mode=="all")foreach(var app in apps)appsPanel.Children.Add(Row(app.DisplayName,()=>engine.Data.Settings.Rules.Any(r=>r.Identifier.Equals(app.Identifier,StringComparison.OrdinalIgnoreCase)),()=>{ToggleRecognizedApplication(app);return Task.CompletedTask;}));
            if(apps.Count==0)appsPanel.Children.Add(new TextBlock{Text=Text("未找到可添加的应用"),Foreground=Brushes.Gray,FontSize=12});
            var browsers=apps.Where(a=>RecognitionRules.Browsers.Contains(a.Identifier,StringComparer.OrdinalIgnoreCase)).ToArray();
            if(browserDiscovery is { IsCompleted:false })
            {Notice("上一次读取尚未结束，请稍后重试");return;}
            browserDiscovery=Task.Run(()=>browsers.Select(recognitionSource.CurrentPage).ToArray());
            var discovery=browserDiscovery;
            var finished=await Task.WhenAny(discovery,Task.Delay(2000));
            if(serial!=recognitionScan || !popup.IsOpen)return;
            if(finished!=discovery){Notice("浏览器暂未响应，请稍后重新识别");return;}
            sites.Children.Clear();
            foreach(var page in await discovery)
            {
                var rule=RecognitionRules.AutomaticWebsite(page.Address);
                if(rule is not null)
                {
                    var preview=RecognitionRules.AddressPreview(page.Address)??Text(UnavailableWebsiteMessage(page));
                    var label=RuleName(preview);label.FontSize=10;label.FontWeight=FontWeights.Normal;
                    Button? button=null;
                    button=Row(preview,()=>rule is not null&&engine.Data.Settings.Websites.Any(r=>RecognitionRules.SameWebsiteKeyword(r,rule.PathContains)),()=>CaptureWebsite(page,fresh=>
                    {
                        rule=RecognitionRules.AutomaticWebsite(fresh.Address);
                        label.Text=RecognitionRules.AddressPreview(fresh.Address)??Text(UnavailableWebsiteMessage(fresh));
                        AutomationProperties.SetName(button!,fresh.BrowserName+" · "+label.Text);
                    }),label,true,page.BrowserName+" · "+preview,"Website-"+page.BrowserIdentifier);
                    websiteButtons.Add(button);sites.Children.Add(button);
                }
                else sites.Children.Add(new TextBlock{Text=page.BrowserName+" · "+Text(UnavailableWebsiteMessage(page)),Foreground=Brushes.Gray,TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new Thickness(3,4,3,4)});
            }
            if(browsers.Length==0)Notice("未找到已打开的浏览器");
        }
        catch(Exception error)when(error is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
        {
            if(serial==recognitionScan && popup.IsOpen)
            {Notice("暂时无法读取，请重新识别。");RecognitionMessage("暂时无法读取，请重新识别。");}
        }
    }
}
