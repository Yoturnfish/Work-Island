using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunProfileContextRefreshLive(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var store=Field<AtomicStore>("store");var color=Field<Button>("RecognitionColorButton");
        var normalOpens=0;var contextRequests=0;var armed=false;var observed=new TaskCompletionSource();
        async Task WaitFile(string name,double seconds)
        {
            var watch=Stopwatch.StartNew();while(!File.Exists(Path.Combine(output,name))){if(watch.Elapsed.TotalSeconds>seconds)throw new TimeoutException(name);await Task.Delay(75);}
        }
        void Write(string name,object value)=>File.WriteAllText(Path.Combine(output,name),JsonSerializer.Serialize(value,AtomicStore.JsonOptions));
        engine.Data.Settings.Paused=true;engine.Data.Settings.Rules=[];engine.Data.Settings.Language="zh";engine.Data.Settings.ReduceMotion=true;engine.Data.Settings.BackgroundStyle="black";engine.ResetObservation();
        Field<DispatcherTimer>("fullscreenTimer").Stop();Field<DispatcherTimer>("focusReturn").Stop();
        window.Title="Work Island · 组合刷新实测 · "+RuntimeCompat.ProcessId;window.ShowInTaskbar=true;window.Topmost=false;window.Left=460;window.Top=300;
        Call("RenameCurrentProfile","旧名称");Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenRecognition",window,new RoutedEventArgs());await Task.Delay(150);window.UpdateLayout();Call("Persist");
        RoutedEventHandler click=(_,_)=>{if(color.ContextMenu?.IsOpen==true)normalOpens++;};
        ContextMenuEventHandler opening=(_,args)=>
        {
            if(!armed)return;contextRequests++;var handled=args.Handled;
            window.Dispatcher.InvokeAsync(async()=>
            {
                try
                {
                    await Task.Delay(240);var menu=color.ContextMenu!;var panel=(StackPanel)((MenuItem)menu.Items[0]).Header;
                    var name=ProfileNameEditor(panel);
                    var description=panel.Children.OfType<TextBlock>().First().Text;
                    var selected=panel.Children.OfType<System.Windows.Controls.Primitives.UniformGrid>().First().Children.OfType<StackPanel>().First().Children.OfType<TextBlock>().Single().Text;
                    var evidence=new{contextRequests,handled,menuOpen=menu.IsOpen,ownerVisible=window.IsVisible,currentTitle=engine.Data.Settings.DisplayTitle,name=name.Text,selected,description};
                    Write("native-observation.json",evidence);
                    check("official actual right click reaches one context opening",contextRequests==1,null);
                    check("ordinary right click opens a visible profile menu",menu.IsOpen&&window.IsVisible,null);
                    check("actual context menu name follows current saved title",name.Text=="当前名称",name.Text);
                    check("actual context menu color caption follows current saved title",selected.Contains("当前名称"),selected);
                    check("actual context menu language follows current settings",description.StartsWith("Choose a color",StringComparison.Ordinal),description);
                    // Capture only this test's actual popup HWND rectangle.
                    if(PresentationSource.FromVisual(menu) is HwndSource source&&WindowsActivity.GetWindowRect(source.Handle,out var rect))
                    {
                        using var bitmap=new System.Drawing.Bitmap(rect.Right-rect.Left,rect.Bottom-rect.Top);
                        using(var graphics=System.Drawing.Graphics.FromImage(bitmap))graphics.CopyFromScreen(rect.Left,rect.Top,0,0,bitmap.Size);
                        bitmap.Save(Path.Combine(output,"context-popup-screen.png"),System.Drawing.Imaging.ImageFormat.Png);
                        Write("capture-bounds.json",new{width=bitmap.Width,height=bitmap.Height,scope="Own actual popup HWND desktop rectangle only; not official screenshot payload."});
                    }
                    observed.TrySetResult();
                }
                catch(Exception error){observed.TrySetException(error);}
            });
        };
        color.AddHandler(Button.ClickEvent,click,true);color.AddHandler(ContextMenuService.ContextMenuOpeningEvent,opening,true);
        try
        {
            Write("ready.json",new{processId=RuntimeCompat.ProcessId,startedUtcTicks=Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString(),windowHandle=new WindowInteropHelper(window).Handle.ToInt64(),window.Title,scope="Await official actual left color click and Escape, then update marker. Title update is a controlled actual WPF editor routed Enter; language is controlled model setting, not physical typing or OS language."});
            await WaitFile("update.txt",180);
            check("official left click created retained menu and Escape closed it",normalOpens==1&&color.ContextMenu is not null&&!color.ContextMenu.IsOpen,null);
            var editor=Field<TextBox>("TitleEditor");editor.Text="当前名称";editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window)!,0,Key.Enter){RoutedEvent=Keyboard.KeyDownEvent});
            engine.Data.Settings.Language="en";Call("ApplyLanguage");Call("Persist");
            check("controlled title edit committed actual current data",store.Load().Settings.DisplayTitle=="当前名称",null);
            var bytes=File.ReadAllBytes(store.Path);var memory=JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions);
            armed=true;Write("updated.json",new{processId=RuntimeCompat.ProcessId,title=engine.Data.Settings.DisplayTitle,language=engine.Data.Settings.Language});
            await observed.Task.WaitAsync(TimeSpan.FromSeconds(90));
            check("actual right-click view refresh does not write or mutate saved data",File.ReadAllBytes(store.Path).AsSpan().SequenceEqual(bytes)&&JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions)==memory,null);
            await WaitFile("finish.txt",90);
        }
        finally
        {
            color.RemoveHandler(Button.ClickEvent,click);color.RemoveHandler(ContextMenuService.ContextMenuOpeningEvent,opening);
            if(color.ContextMenu is {} menu)menu.IsOpen=false;
            await Task.Delay(350);Call("ShowPage","home",false);Call("CollapseToTimer");Field<DispatcherTimer>("focusReturn").Stop();await Task.Delay(70);
        }
    }
}
