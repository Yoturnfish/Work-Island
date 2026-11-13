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
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunProfileSaveChecks(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;var store=Field<AtomicStore>("store");settings.Paused=true;settings.Rules=[];settings.ReduceMotion=true;
        var start=DateTimeOffset.Now.AddHours(-1);engine.Data.Sessions.Add(new(){ApplicationIdentifier="profile-history.exe",ApplicationName="Profile history",StartedAt=start,EndedAt=start.AddSeconds(120),EffectiveSeconds=120,DayKey=LogicalDay.Key(start,engine.Zone,settings.DayResetMinutes)});
        engine.ResetObservation();Call("Render");Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenRecognition",window,new RoutedEventArgs());await Task.Delay(60);Call("Persist");
        var original=settings.ActiveProfileId;var history=JsonSerializer.Serialize(engine.Data.Sessions,AtomicStore.JsonOptions);var before=File.ReadAllBytes(store.Path);var observations=new List<object>();
        StackPanel Panel()=> (StackPanel)((MenuItem)Field<Button>("RecognitionColorButton").ContextMenu!.Items[0]).Header;
        ContextMenu Menu()=>Field<Button>("RecognitionColorButton").ContextMenu!;
        TextBlock Notice()=>Panel().Children.OfType<TextBlock>().Last();
        TextBox Name()=>ProfileNameEditor(Panel());
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var menus=new List<ContextMenu>();
        void Open(){Call("OpenProfilePopover");menus.Add(Menu());}
        void Failure(string name,Action action,Func<bool> changed)
        {
            action();var text=Notice().Text;observations.Add(new{name,text,settings.ActiveProfileId,settings.DisplayTitle});
            check(name+" retains its in-memory edit",changed(),null);
            check(name+" reports pending save inside current profile popup",Menu().IsOpen&&text.Contains("保存失败")&&text.Contains("内存"),text);
            check(name+" retains recognition page and pause",ReferenceEquals(Field<FrameworkElement>("activePage"),Field<FrameworkElement>("RecognitionPage"))&&engine.Current.State=="paused",null);
        }
        Open();
        void Color(int index)=>Click(Panel().Children.OfType<UniformGrid>().First().Children.OfType<StackPanel>().ElementAt(index).Children.OfType<Button>().Single());
        using(var locked=new FileStream(store.Path,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            Failure("rename binding",()=>Name().Text="未写入的剪辑组合",()=>settings.DisplayTitle=="未写入的剪辑组合");
            Failure("progress style",()=>Click(Panel().Children.OfType<StackPanel>().Single().Children.OfType<Button>().Last()),()=>settings.ProgressStyle=="waveform");
            Failure("color slot",()=>Color(1),()=>settings.Accent=="blue");
            Failure("blue rename",()=>Name().Text="蓝色未写入组合",()=>settings.DisplayTitle=="蓝色未写入组合");
            Failure("activate default",()=>Color(0),()=>settings.ActiveProfileId==original&&settings.DisplayTitle=="未写入的剪辑组合");
            check("failed profile changes never modify primary bytes",File.ReadAllBytes(store.Path).SequenceEqual(before),null);
            check("failed profile changes preserve history",JsonSerializer.Serialize(engine.Data.Sessions,AtomicStore.JsonOptions)==history,null);
            check("pending-save panel has no creation or deletion row",Panel().Children.OfType<UniformGrid>().Count()==1,null);
            var oldNotice=Notice();Menu().IsOpen=false;Open();await Task.Delay(180);
            check("reopened profile popup retains pending status despite old Closed",Notice().Text.Contains("保存失败"),Notice().Text);
            settings.Language="en";Call("ApplyLanguage");Open();
            check("pending profile save follows English on reopen",Notice().Text.Contains("Save failed")&&Notice().Text.Contains("memory"),Notice().Text);
            settings.Language="zh";Call("ApplyLanguage");Open();
        }
        Call("Persist");
        var saved=store.Load();check("recovery saves current profile title and progress style",saved.Settings.DisplayTitle=="未写入的剪辑组合"&&saved.Settings.ProgressStyle=="waveform",null);
        check("recovery keeps all history intact",JsonSerializer.Serialize(saved.Sessions,AtomicStore.JsonOptions)==history,null);
        check("background recovery updates current popup to saved",Notice().Text=="更改已保存。",Notice().Text);
        var bytes=File.ReadAllBytes(store.Path);
        using(var locked=new FileStream(store.Path,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            Failure("clear default name",()=>Name().Text="",()=>settings.DisplayTitle=="");
            check("failed clear retains original saved bytes",File.ReadAllBytes(store.Path).SequenceEqual(bytes),null);
            Name().ApplyTemplate();check("failed clear still presents default Screen work time",Name().Template.FindName("NameHint",Name()) is TextBlock {Text:"屏幕工作时间",Visibility:Visibility.Visible},null);
        }
        Call("Persist");check("recovered empty default remains empty on disk",store.Load().Settings.DisplayTitle=="",null);
        check("recovered empty default keeps its protected profile and history",ColorProfilesService.Active(store.Load().Settings)?.IsDefault==true&&JsonSerializer.Serialize(store.Load().Sessions,AtomicStore.JsonOptions)==history,null);
        Menu().IsOpen=false;settings.Language="en";Call("ApplyLanguage");Open();
        check("recovered popup notice follows English",Notice().Text=="Changes saved.",Notice().Text);
        foreach(var ownedMenu in menus)ownedMenu.IsOpen=false;
        await Task.Delay(250);
        File.WriteAllText(Path.Combine(output,"profile-save.json"),JsonSerializer.Serialize(new{appSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),coreSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(TrackingEngine).Assembly.Location))),observations,scope="Actual FileStream primary-file locks and actual own WPF profile controls/events, in-memory changes plus durable background recovery. No native input or user-data mutations; unchanged Mac text binding, delete confirmation and missing-active-ID semantics."},AtomicStore.JsonOptions));
    }
}

