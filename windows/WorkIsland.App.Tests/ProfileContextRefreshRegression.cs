using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunProfileContextRefresh(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var store=Field<AtomicStore>("store");var color=Field<Button>("RecognitionColorButton");
        var menus=new List<ContextMenu>();var observations=new List<object>();
        StackPanel Panel()=> (StackPanel)((MenuItem)color.ContextMenu!.Items[0]).Header;
        TextBox Name()=>ProfileNameEditor(Panel());
        string Caption()=>Panel().Children.OfType<System.Windows.Controls.Primitives.UniformGrid>().First().Children.OfType<StackPanel>().First().Children.OfType<TextBlock>().Single().Text;
        void Track(){if(color.ContextMenu is {} menu&&!menus.Contains(menu))menus.Add(menu);}
        void Close(){Track();foreach(var menu in menus)menu.IsOpen=false;Field<DispatcherTimer>("focusReturn").Stop();}
        void ContextRequest()
        {
            var args=(ContextMenuEventArgs)RuntimeHelpers.GetUninitializedObject(typeof(ContextMenuEventArgs));
            args.RoutedEvent=ContextMenuService.ContextMenuOpeningEvent;args.Source=color;color.RaiseEvent(args);
            // Model only the service's default step after its real routed event.
            // Native right-click verification is separate from this fixture.
            if(!args.Handled&&color.ContextMenu is {} retained)retained.IsOpen=true;
            Track();
        }
        engine.Data.Settings.Paused=true;engine.Data.Settings.Rules=[];engine.Data.Settings.ReduceMotion=true;engine.Data.Settings.Language="zh";engine.ResetObservation();
        Field<DispatcherTimer>("fullscreenTimer").Stop();Field<DispatcherTimer>("focusReturn").Stop();
        try
        {
            Call("RenameCurrentProfile","之前名称");Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenRecognition",window,new RoutedEventArgs());
            Call("OpenProfilePopover");Track();
            check("initial left-open menu exposes saved original profile name",color.ContextMenu!.IsOpen&&Name().Text=="之前名称",Name().Text);
            Close();await Task.Delay(220);
            var editor=Field<TextBox>("TitleEditor");editor.Text="主界面新名称";
            editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window)!,0,Key.Enter){RoutedEvent=Keyboard.KeyDownEvent});
            check("actual title editor commit updates active model and disk",engine.Data.Settings.DisplayTitle=="主界面新名称"&&store.Load().Settings.DisplayTitle=="主界面新名称",null);
            ContextRequest();
            check("context reopen displays current title editor value",Name().Text=="主界面新名称",Name().Text);
            check("context reopen color caption displays current title",Caption()=="主界面新名称",Caption());
            observations.Add(new{phase="rename",name=Name().Text,caption=Caption()});Close();await Task.Delay(220);
            engine.Data.Settings.Language="en";Call("ApplyLanguage");Call("Persist");
            var saved=File.ReadAllBytes(store.Path);var memory=JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions);
            ContextRequest();
            check("context reopen refreshes explanatory language",Panel().Children.OfType<TextBlock>().First().Text.StartsWith("Choose a color",StringComparison.Ordinal),Panel().Children.OfType<TextBlock>().First().Text);
            check("context reopen omits the removed naming label",!Panel().Children.OfType<DockPanel>().Any(),null);
            check("context reopen does not restore removed create or delete actions",Panel().Children.OfType<System.Windows.Controls.Primitives.UniformGrid>().Count()==1,null);
            check("context reopen refreshes editor accessibility language",AutomationProperties.GetName(Name())=="Name this theme",AutomationProperties.GetName(Name()));
            check("context reopen retains current profile and title",ColorProfilesService.Active(engine.Data.Settings)?.Title=="主界面新名称"&&Name().Text=="主界面新名称",Name().Text);
            check("view refresh preserves all saved settings and bytes",File.ReadAllBytes(store.Path).AsSpan().SequenceEqual(saved)&&JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions)==memory,null);
            observations.Add(new{phase="language",name=Name().Text,description=Panel().Children.OfType<TextBlock>().First().Text});
            Close();await Task.Delay(220);ContextRequest();
            check("repeated context requests keep one open tracked menu",menus.Count(m=>m.IsOpen)==1,null);
            check("context refresh retains the protected default profile",ColorProfilesService.Active(engine.Data.Settings)?.IsDefault==true,null);
            File.WriteAllText(Path.Combine(output,"profile-context-refresh.json"),JsonSerializer.Serialize(new{observations,scope="Actual WPF title editor routed Enter, saved AtomicStore, owner context opening event plus explicit default service step. Controlled language setting. Native actual right click is separate; no user data or system settings."},AtomicStore.JsonOptions));
        }
        finally
        {
            Close();await Task.Delay(350);Call("ShowPage","home",false);Call("CollapseToTimer");Field<DispatcherTimer>("focusReturn").Stop();await Task.Delay(70);
        }
    }
}
