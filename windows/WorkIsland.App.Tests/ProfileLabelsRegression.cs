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
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunProfileLabelsChecks(IslandWindow window,string output,Action<string,bool,object?> check,bool review=false)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;var store=Field<AtomicStore>("store");settings.Paused=true;settings.Rules=[];settings.ReduceMotion=true;
        if(review){settings.BackgroundStyle="black";window.Left=320;window.Top=260;window.Topmost=true;window.Title="Work Island · 命名同步验收";}
        engine.ResetObservation();Call("CreateProfile");var first=settings.ActiveProfileId!;Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenRecognition",window,new RoutedEventArgs());await Task.Delay(70);Call("OpenProfilePopover");
        var menus=new List<ContextMenu>();
        StackPanel Panel()=> (StackPanel)((MenuItem)Field<Button>("RecognitionColorButton").ContextMenu!.Items[0]).Header;
        ContextMenu Menu()=>Field<Button>("RecognitionColorButton").ContextMenu!;
        TextBox Name()=>ProfileNameEditor(Panel());
        TextBlock Blue()=>Panel().Children.OfType<UniformGrid>().First().Children.OfType<StackPanel>().ElementAt(1).Children.OfType<TextBlock>().Single();
        TextBlock Green()=>Panel().Children.OfType<UniformGrid>().First().Children.OfType<StackPanel>().First().Children.OfType<TextBlock>().Single();
        void Color(int index)=>Panel().Children.OfType<UniformGrid>().First().Children.OfType<StackPanel>().ElementAt(index).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Open(){Call("OpenProfilePopover");menus.Add(Menu());}
        menus.Add(Menu());var initialName=Name();var initialCaption=Blue();var profileCount=settings.ColorProfiles.Count;
        window.Activate();initialName.Focus();Keyboard.Focus(initialName);initialName.Text="  剪辑🎬  ";
        check("typing updates active theme title without Enter",ColorProfilesService.Active(settings)?.Title=="剪辑🎬",null);
        check("typing updates mapped color caption without Enter",Blue().Text=="剪辑🎬",Blue().Text);
        check("name section contains only one input and no selector",Panel().Children.OfType<TextBox>().Count()==1&&!Panel().Children.OfType<ComboBox>().Any(),null);
        check("label sync preserves current raw editing draft",ReferenceEquals(initialName,Name())&&initialName.Text=="  剪辑🎬  ",initialName.Text);
        check("label sync does not rebuild caption or change active profile",ReferenceEquals(initialCaption,Blue())&&settings.ActiveProfileId==first&&settings.ColorProfiles.Count==profileCount,null);
        check("label sync keeps actual editor keyboard focus",initialName.IsKeyboardFocusWithin,null);
        check("live labels match persisted normalized title",store.Load().Settings.DisplayTitle=="剪辑🎬"&&ColorProfilesService.Active(store.Load().Settings)!.Title=="剪辑🎬",null);
        if(review)
        {
            File.WriteAllText(Path.Combine(output,"review-ready.json"),JsonSerializer.Serialize(new{processId=Environment.ProcessId,windowHandle=((System.Windows.Interop.HwndSource)PresentationSource.FromVisual(window)).Handle.ToInt64(),window.Title,draft=initialName.Text,caption=Blue().Text,scope="Controlled TextBox.Text edit through production TextChanged, screenshot review only. No native typing or IME acceptance."},AtomicStore.JsonOptions));
            var watch=System.Diagnostics.Stopwatch.StartNew();
            while(watch.Elapsed.TotalSeconds<180&&!File.Exists(Path.Combine(output,"finish-review.txt")))await Task.Delay(100);
        }
        initialName.Text="";check("empty ordinary color name keeps empty title and caption",ColorProfilesService.Active(settings)?.Title==""&&Blue().Text=="",Blue().Text);
        initialName.Text="历史";check("user named theme is not translated as a fixed UI label",ColorProfilesService.Active(settings)?.Title=="历史"&&Blue().Text=="历史",null);
        var bytes=File.ReadAllBytes(store.Path);
        using(var locked=new FileStream(store.Path,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            initialName.Text="内存名称";
            check("failed-save memory rename still updates visible aliases",ColorProfilesService.Active(settings)?.Title=="内存名称"&&Blue().Text=="内存名称",null);
            check("memory label refresh preserves pending-save notice and original bytes",Panel().Children.OfType<TextBlock>().Last().Text.Contains("保存失败")&&File.ReadAllBytes(store.Path).SequenceEqual(bytes),null);
        }
        Call("Persist");check("background recovery persists the same visible name",store.Load().Settings.DisplayTitle=="内存名称"&&Blue().Text=="内存名称",null);
        var paletteBefore=Blue().Text;Call("CreateProfile");Menu().IsOpen=false;Open();var second=settings.ActiveProfileId!;Name().Text="第二组合";
        check("same-color duplicate rename only changes the active profile",settings.ColorProfiles.Single(p=>p.Id==second).Title=="第二组合"&&settings.ColorProfiles.Single(p=>p.Id==first).Title=="内存名称",null);
        check("same-color caption follows the selected current profile",Blue().Text=="第二组合"&&paletteBefore=="内存名称",Blue().Text);
        var defaultId=settings.ColorProfiles.Single(p=>p.IsDefault).Id;Color(0);Name().Text="默认自定";
        check("default theme keeps its fixed caption and editable custom name",settings.ActiveProfileId==defaultId&&Green().Text=="默认"&&Name().Text=="默认自定"&&store.Load().Settings.DisplayTitle=="默认自定",Green().Text);
        Name().Text="";check("empty default color caption remains Default without changing the editor",Green().Text=="默认"&&Name().Text==""&&store.Load().Settings.DisplayTitle=="",Green().Text);
        Menu().IsOpen=false;settings.Language="en";Call("ApplyLanguage");Open();Name().Text="历史";
        check("English UI separates Default caption from the unchanged custom Chinese editor name",Green().Text=="Default"&&Name().Text=="历史"&&store.Load().Settings.DisplayTitle=="历史",Green().Text);
        Color(1);Color(1);Name().Text="";check("same-color cycling reaches the second theme and preserves an empty name",settings.ActiveProfileId==second&&Name().Text==""&&Blue().Text=="",null);
        foreach(var menu in menus)menu.IsOpen=false;await Task.Delay(250);
        File.WriteAllText(Path.Combine(output,"profile-labels.json"),JsonSerializer.Serialize(new{appSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),coreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(TrackingEngine).Assembly.Location))),scope="Actual WPF TextChanged and palette controls, real own-data file lock, duplicate-color/default/localization cases. Direct control edits, not native typing or physical IME acceptance; raw draft, focus and profile identities checked."},AtomicStore.JsonOptions));
    }
}
