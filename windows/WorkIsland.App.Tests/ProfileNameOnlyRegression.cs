using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    internal static TextBox ProfileNameEditor(StackPanel panel)=>panel.Children.OfType<TextBox>().SingleOrDefault()
        ??panel.Children.OfType<DockPanel>().Single().Children.OfType<TextBox>().Single();

    private static async Task RunProfileNameOnly(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var settings=engine.Data.Settings;var store=Field<AtomicStore>("store");
        var color=Field<Button>("RecognitionColorButton");var menus=new List<ContextMenu>();
        StackPanel Panel()=>(StackPanel)((MenuItem)color.ContextMenu!.Items[0]).Header;
        TextBox Name()=>ProfileNameEditor(Panel());
        TextBlock? Hint(){Name().ApplyTemplate();return Name().Template.FindName("NameHint",Name()) as TextBlock;}
        TextBlock Caption(int index)=>Panel().Children.OfType<UniformGrid>().First().Children.OfType<StackPanel>().ElementAt(index).Children.OfType<TextBlock>().Single();
        void Color(int index)=>Panel().Children.OfType<UniformGrid>().First().Children.OfType<StackPanel>().ElementAt(index).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Open(){Call("OpenProfilePopover");menus.Add(color.ContextMenu!);window.UpdateLayout();}
        async Task Capture(string file)
        {
            window.UpdateLayout();await Task.Delay(220);
            if(PresentationSource.FromVisual(color.ContextMenu!) is not HwndSource source||!WindowsActivity.GetWindowRect(source.Handle,out var rect))throw new InvalidOperationException("Own popup HWND absent");
            using var bitmap=new System.Drawing.Bitmap(rect.Right-rect.Left,rect.Bottom-rect.Top);
            using(var graphics=System.Drawing.Graphics.FromImage(bitmap))graphics.CopyFromScreen(rect.Left,rect.Top,0,0,bitmap.Size);
            bitmap.Save(Path.Combine(output,file),System.Drawing.Imaging.ImageFormat.Png);
        }
        settings.Paused=true;settings.Rules=[];settings.Language="zh";settings.ReduceMotion=true;engine.ResetObservation();
        Field<DispatcherTimer>("fullscreenTimer").Stop();Field<DispatcherTimer>("focusReturn").Stop();
        window.Left=460;window.Top=300;window.Topmost=true;
        var defaultId=settings.ColorProfiles.Single(p=>p.IsDefault).Id;
        try
        {
            Call("ActivateProfile",defaultId);Call("RenameCurrentProfile","");Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenRecognition",window,new RoutedEventArgs());Open();
            check("profile panel has no standalone theme selector",!Panel().Children.OfType<ComboBox>().Any(),null);
            check("profile naming section is one full-width input without a label",Panel().Children.OfType<TextBox>().Count()==1&&!Panel().Children.OfType<DockPanel>().Any(),null);
            check("empty default editor visibly falls back to Screen work time",Hint() is {Visibility:Visibility.Visible,Text:"屏幕工作时间"},Hint()?.Text);
            check("first green caption identifies the default theme",Caption(0).Text=="默认",Caption(0).Text);
            check("default hint does not rewrite stored empty title",Name().Text==""&&store.Load().Settings.DisplayTitle=="",null);
            await Capture("default-zh-screen.png");
            Name().Text="我的剪辑";
            check("typing preserves default caption while keeping current custom editor name",Hint()?.Visibility==Visibility.Collapsed&&Caption(0).Text=="默认"&&Name().Text=="我的剪辑"&&store.Load().Settings.DisplayTitle=="我的剪辑",null);
            Name().Text="";
            check("clearing restores default hint immediately while preserving editability",Hint() is {Visibility:Visibility.Visible,Text:"屏幕工作时间"}&&Name().IsEnabled,null);
            var bytes=File.ReadAllBytes(store.Path);color.ContextMenu!.IsOpen=false;await Task.Delay(220);Open();
            check("reopening empty default preserves fallback and saved bytes",Hint()?.Text=="屏幕工作时间"&&File.ReadAllBytes(store.Path).AsSpan().SequenceEqual(bytes),null);
            check("profile panel has no create or delete action row",Panel().Children.OfType<UniformGrid>().Count()==1,null);
            Call("CreateProfile");var first=settings.ActiveProfileId!;Call("RenameCurrentProfile","蓝一");Call("CreateProfile");var second=settings.ActiveProfileId!;Call("RenameCurrentProfile","蓝二");
            color.ContextMenu!.IsOpen=false;Open();Color(1);
            check("color controls still reach an existing same-color theme",settings.ActiveProfileId==first&&Name().Text=="蓝一",null);
            Color(1);check("repeated color click reaches the other same-color theme without selector",settings.ActiveProfileId==second&&Name().Text=="蓝二",null);
            check("same-color switching preserves both records and IDs",store.Load().Settings.ColorProfiles.Any(p=>p.Id==first&&p.Title=="蓝一")&&store.Load().Settings.ColorProfiles.Any(p=>p.Id==second&&p.Title=="蓝二"),null);
            Color(0);Name().Text="";color.ContextMenu!.IsOpen=false;settings.Language="en";Call("ApplyLanguage");Call("Persist");Open();
            check("English default hint and separate default caption follow UI language",Hint() is {Visibility:Visibility.Visible,Text:"Screen work time"}&&Caption(0).Text=="Default",Hint()?.Text);
            await Capture("default-en-screen.png");
            check("name-only panel retains just the two progress previews below its input",Panel().Children.OfType<StackPanel>().Single().Children.OfType<Button>().Count()==2&&Panel().Children.OfType<UniformGrid>().Count()==1,null);
            File.WriteAllText(Path.Combine(output,"profile-name-only.json"),JsonSerializer.Serialize(new{scope="Actual WPF panel shape, TextChanged, saved empty-title fallback, protected default and duplicate-color switching. Controlled inputs and language; real native interface verification is separate."},AtomicStore.JsonOptions));
        }
        finally{foreach(var menu in menus)menu.IsOpen=false;await Task.Delay(350);Call("ShowPage","home",false);Call("CollapseToTimer");Field<DispatcherTimer>("focusReturn").Stop();}
    }
}
