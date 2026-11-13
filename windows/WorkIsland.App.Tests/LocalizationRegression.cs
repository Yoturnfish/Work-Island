using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunLocalizationChecks(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var saved=System.Text.Json.JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions);
        var folder=Path.Combine(output,"localization");Directory.CreateDirectory(folder);
        var store=Field<AtomicStore>("store");
        string Label(string field)=>Field<Button>(field).Content switch {StackPanel stack=>stack.Children.OfType<TextBlock>().Last().Text,Grid grid=>grid.Children.OfType<TextBlock>().Single().Text,_=>""};
        void SelectLanguage(string code){var picker=Field<ComboBox>("LanguagePicker");picker.SelectedItem=picker.Items.Cast<ComboBoxItem>().Single(i=>i.Tag?.ToString()==code);}
        async Task Page(string page,string capture){Call("ShowPage",page,false);await Task.Delay(300);window.UpdateLayout();Call("SaveScreenRegion",Path.Combine(folder,capture+".png"),23);}
        try
        {
            engine.Data.Settings.ReduceMotion=true;engine.Data.Settings.BackgroundStyle="black";engine.Data.Settings.Rules=[new("custom.exe","历史")];
            Call("RebuildRules");Call("ToggleExpanded",null!,new RoutedEventArgs());await Task.Delay(400);Field<DispatcherTimer>("focusReturn").Stop();
            Call("OpenRecognition",null!,new RoutedEventArgs());SelectLanguage("en");await Page("recognition","before-or-after-en-recognition");
            check("English recognition includes auto-detect and both rule headers",Label("AutomaticRecognitionButton")=="Auto-detect"&&Label("ApplicationRulesButton")=="Applications"&&Label("WebsiteRulesButton")=="Websites",new{automatic=Label("AutomaticRecognitionButton"),apps=Label("ApplicationRulesButton"),websites=Label("WebsiteRulesButton")});
            var custom=(TextBlock)Field<StackPanel>("RuleRows").Children.OfType<Grid>().Single().Children.OfType<CheckBox>().Single().Content;
            check("language switch preserves custom rule name even when it matches an interface key",custom.Text=="历史",custom.Text);
            Call("RecognitionMessage","这个应用已在列表中。勾选它即可启用。");
            check("English duplicate rule feedback is localized in place",Field<TextBlock>("RecognitionStatus").Text=="This app is already listed. Select it to enable tracking.",Field<TextBlock>("RecognitionStatus").Text);
            Call("RecognitionMessage","隐私窗口不读取");
            check("English browser privacy feedback is localized",Field<TextBlock>("RecognitionStatus").Text=="Private windows are not read",Field<TextBlock>("RecognitionStatus").Text);
            check("English rule accessibility follows expanded state",AutomationProperties.GetName(Field<Button>("ApplicationRulesButton")).StartsWith("Expand ")||AutomationProperties.GetName(Field<Button>("ApplicationRulesButton")).StartsWith("Collapse "),AutomationProperties.GetName(Field<Button>("ApplicationRulesButton")));
            await Page("settings","en-settings");Field<ScrollViewer>("PageScroller").ScrollToBottom();await Task.Delay(100);
            Call("SaveScreenRegion",Path.Combine(folder,"en-settings-bottom.png"),23);
            var exit=Field<Button>("ExitButton");var exitLabel=((StackPanel)((Grid)exit.Content).Children[0]).Children.OfType<TextBlock>().Single();
            check("English exit action is translated",exitLabel.Text=="Quit Work Island",exitLabel.Text);
            var tray=Field<System.Windows.Forms.NotifyIcon>("tray");
            check("English tray actions use current interface language",tray.ContextMenuStrip!.Items[0].Text=="Show Work Island"&&tray.ContextMenuStrip.Items[1].Text=="Pause / Resume"&&tray.ContextMenuStrip.Items[3].Text=="Quit",tray.ContextMenuStrip.Items.Cast<System.Windows.Forms.ToolStripItem>().Select(i=>i.Text).ToArray());
            SelectLanguage("zh");await Page("recognition","zh-recognition");
            check("existing recognition notice follows language changes",Field<TextBlock>("RecognitionStatus").Text=="隐私窗口不读取",Field<TextBlock>("RecognitionStatus").Text);
            check("English to Chinese returns static labels without rebuilding user names",Label("AutomaticRecognitionButton")=="自动识别"&&Label("ApplicationRulesButton")=="应用程序"&&custom.Text=="历史",null);
            Call("ProfileChanged");SelectLanguage("en");await Task.Delay(50);SelectLanguage("zh");
            check("rebuilding rules while English does not translate user text on later Chinese switch",Field<StackPanel>("RuleRows").Children.OfType<Grid>().Single().Children.OfType<CheckBox>().Single().Content is TextBlock name&&name.Text=="历史",null);
            check("language preference persists without changing custom rule",store.Load().Settings.Language=="zh"&&store.Load().Settings.Rules.Single().DisplayName=="历史",null);
            check("main time typography remains Consolas Bold 14 DIP",Field<TextBlock>("TimeLabel").FontFamily.Source=="Consolas"&&Field<TextBlock>("TimeLabel").FontWeight==FontWeights.Bold&&Field<TextBlock>("TimeLabel").FontSize==14,null);
            var englishData=new AppState{Settings=new(){Language="en",Rules=[new("localization-startup.exe","设置")],WindowX=500,WindowY=350,ReduceMotion=true}};
            var englishWindow=new IslandWindow(new AtomicStore(Path.Combine(folder,"english-startup.json")),englishData,false,null,startupRegistration:new IsolatedStartupRegistration(Path.Combine(folder,"english-startup-registration.json"),"isolated localization"),recognitionSource:new RecognitionFixture(),applicationPicker:new ApplicationPickerFixture(),fullscreenSource:new FullscreenFixture());
            try
            {
                englishWindow.ShowActivated=false;englishWindow.Loaded+=(_,_)=>((DispatcherTimer)typeof(IslandWindow).GetField("timer",Flags)!.GetValue(englishWindow)!).Stop();englishWindow.Show();await Task.Delay(100);
                typeof(IslandWindow).GetMethod("ToggleExpanded",Flags)!.Invoke(englishWindow,[null,new RoutedEventArgs()]);
                typeof(IslandWindow).GetMethod("OpenRecognition",Flags)!.Invoke(englishWindow,[null,new RoutedEventArgs()]);await Task.Delay(150);
                var englishAuto=(Button)typeof(IslandWindow).GetField("AutomaticRecognitionButton",Flags)!.GetValue(englishWindow)!;
                var englishRows=(StackPanel)typeof(IslandWindow).GetField("RuleRows",Flags)!.GetValue(englishWindow)!;
                check("English cold startup translates fixed labels and preserves Chinese custom names",((StackPanel)englishAuto.Content).Children.OfType<TextBlock>().Single().Text=="Auto-detect"&&englishRows.Children.OfType<Grid>().Single().Children.OfType<CheckBox>().Single().Content is TextBlock customName&&customName.Text=="设置",null);
            }
            finally{typeof(IslandWindow).GetField("exiting",Flags)!.SetValue(englishWindow,true);englishWindow.Close();}
        }
        finally
        {
            var restored=System.Text.Json.JsonSerializer.Deserialize<AppState>(saved,AtomicStore.JsonOptions)!;
            engine.Data.Settings=restored.Settings;engine.Data.Sessions.Clear();engine.Data.Sessions.AddRange(restored.Sessions);engine.ResetObservation();
            typeof(IslandWindow).GetField("loadingPreferences",Flags)!.SetValue(window,true);Call("LoadSettingsInputs");
            var picker=Field<ComboBox>("LanguagePicker");picker.SelectedItem=picker.Items.Cast<ComboBoxItem>().Single(i=>i.Tag?.ToString()==restored.Settings.Language);
            typeof(IslandWindow).GetField("loadingPreferences",Flags)!.SetValue(window,false);Call("ProfileChanged");Call("ShowPage","home",false);Call("CollapseToTimer");
        }
    }
}
