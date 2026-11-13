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
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunRecognitionSaveChecks(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;var store=Field<AtomicStore>("store");
        settings.ReduceMotion=true;settings.Language="zh";settings.Rules=[new("remove-existing.exe","Existing app")];settings.Websites=[RecognitionRules.Website("remove")!];
        Call("ApplyLanguage");Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenRecognition",window,new RoutedEventArgs());await Task.Delay(100);Field<DispatcherTimer>("focusReturn").Stop();
        if(!(bool)Call("Persist")!)throw new InvalidOperationException("Initial fixture save failed");
        var before=File.ReadAllBytes(store.Path);var observations=new List<object>();var message=Field<TextBlock>("RecognitionStatus");
        void Attempt(string name,Action action,Func<bool> memory)
        {
            Call("RecognitionMessage","");action();Field<DispatcherTimer>("focusReturn").Stop();
            observations.Add(new{name,message=message.Text,memoryApplied=memory()});
            check(name+" keeps memory change",memory(),null);
            check(name+" keeps actual recognition page",Field<FrameworkElement>("activePage").Name=="RecognitionPage",null);
            check(name+" reports pending save rather than success",message.Text.Contains("保存失败")&&message.Text.Contains("内存"),message.Text);
        }
        using(var locked=new FileStream(store.Path,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            Attempt("automatic application add",()=>Call("ToggleRecognizedApplication",new RunningApplication("added-lock.exe","Added app",0)),()=>settings.Rules.Any(r=>r.Identifier=="added-lock.exe"));
            Attempt("automatic application removal",()=>Call("ToggleRecognizedApplication",new RunningApplication("remove-existing.exe","Existing app",0)),()=>settings.Rules.All(r=>r.Identifier!="remove-existing.exe"));
            Attempt("automatic website add",()=>Call("ToggleRecognizedWebsite",new BrowserPage("msedge.exe","Edge",0,"https://added.example/",null)),()=>settings.Websites.Any(r=>r.DisplayName=="added"));
            Attempt("automatic website removal",()=>Call("ToggleRecognizedWebsite",new BrowserPage("msedge.exe","Edge",0,"https://remove.example/",null)),()=>settings.Websites.All(r=>r.DisplayName!="remove"));
            Attempt("manual application add",()=>{Field<TextBox>("RuleInput").Text="manual-lock.exe";Call("AddRule",null,new RoutedEventArgs());},()=>settings.Rules.Any(r=>r.Identifier=="manual-lock.exe"));
            Attempt("manual website add",()=>{Field<TextBox>("WebsiteInput").Text="manual.example";Call("AddWebsite",null,new RoutedEventArgs());},()=>settings.Websites.Any(r=>r.DisplayName=="manual.example"));
            Attempt("selected executable add",()=>{Field<ApplicationPickerFixture>("applicationPicker").NextSelection=RuntimeCompat.ProcessPath;Field<Button>("AddApplicationSelectionButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));},()=>settings.Rules.Any(r=>r.Identifier==Path.GetFileName(RuntimeCompat.ProcessPath)));
            Grid Row(string rows,string name)=>Field<StackPanel>(rows).Children.OfType<Grid>().Single(row=>row.Children.OfType<CheckBox>().Any(c=>((TextBlock)c.Content).Text==name));
            void Disable(string rows,string name){var box=Row(rows,name).Children.OfType<CheckBox>().Single();box.IsChecked=false;box.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
            Attempt("application checkbox",()=>Disable("RuleRows","Added app"),()=>!settings.Rules.Single(r=>r.Identifier=="added-lock.exe").IsEnabled);
            Attempt("website checkbox",()=>Disable("WebsiteRows","added"),()=>!settings.Websites.Single(r=>r.DisplayName=="added").IsEnabled);
            Attempt("application trash",()=>Row("RuleRows","manual-lock").Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)),()=>settings.Rules.All(r=>r.Identifier!="manual-lock.exe"));
            Attempt("website trash",()=>Row("WebsiteRows","manual.example").Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)),()=>settings.Websites.All(r=>r.DisplayName!="manual.example"));
            check("real primary lock preserves exact persisted bytes",File.ReadAllBytes(store.Path).SequenceEqual(before),null);
            window.UpdateLayout();Call("SaveImage2x",Path.Combine(output,"recognition-pending-save.png"));
            settings.Language="en";Call("ApplyLanguage");
            check("pending recognition save feedback follows English language",message.Text.Contains("Save failed")&&message.Text.Contains("memory"),message.Text);
        }
        var recovered=(bool)Call("Persist")!;var saved=store.Load();
        check("storage recovery durably saves all in-memory application rules",recovered&&saved.Settings.Rules.Select(r=>(r.Identifier,r.IsEnabled)).SequenceEqual(settings.Rules.Select(r=>(r.Identifier,r.IsEnabled))),null);
        check("storage recovery durably saves all in-memory website rules",saved.Settings.Websites.Select(r=>(r.Identifier,r.IsEnabled)).SequenceEqual(settings.Websites.Select(r=>(r.Identifier,r.IsEnabled))),null);
        check("successful background retry replaces pending-save feedback",message.Text=="Changes saved.",message.Text);
        settings.Language="zh";Call("ApplyLanguage");
        check("recovered feedback follows Chinese language",message.Text=="更改已保存。",message.Text);
        Call("ToggleRecognizedApplication",new RunningApplication("recovered-save.exe","Recovered app",0));
        check("subsequent successful add reports success and persists",message.Text.StartsWith("已添加 ",StringComparison.Ordinal)&&store.Load().Settings.Rules.Any(r=>r.Identifier=="recovered-save.exe"),message.Text);
        string validationMessage;
        using(var locked=new FileStream(store.Path,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            Call("ToggleRecognizedApplication",new RunningApplication("retry-before-validation.exe","Pending app",0));
            Field<TextBox>("WebsiteInput").Text="??";Call("AddWebsite",null,new RoutedEventArgs());validationMessage=message.Text;
            check("newer validation message is displayed after pending edit",validationMessage.StartsWith("请输入网站",StringComparison.Ordinal),validationMessage);
        }
        Call("Persist");
        check("background recovery preserves newer unrelated feedback",message.Text==validationMessage&&store.Load().Settings.Rules.Any(r=>r.Identifier=="retry-before-validation.exe"),message.Text);
        File.WriteAllText(Path.Combine(output,"recognition-save.json"),JsonSerializer.Serialize(new{productDllSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),observations,scope="Actual FileStream primary-file lock, production recognition mutation handlers and durable recovery. Synthetic application/browser identities and application-picker provider; no real browser, chooser input or user data."},AtomicStore.JsonOptions));
    }
}

