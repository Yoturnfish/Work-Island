using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;
using Drawing=System.Drawing;

internal static partial class Program
{
    private static async Task RunModeFullscreenBoundaryChecks(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        void Set(string name,object? value)=>typeof(IslandWindow).GetField(name,Flags)!.SetValue(window,value);
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var source=Field<FullscreenFixture>("fullscreenSource");var engine=Field<TrackingEngine>("engine");var store=Field<AtomicStore>("store");
        var picker=Field<ComboBox>("DisplayPicker");var original=JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions);
        var focus=Field<DispatcherTimer>("focusReturn");var polling=Field<DispatcherTimer>("fullscreenTimer");polling.Stop();
        var probe=new Window{Width=420,Height=155,Left=440,Top=260,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowActivated=false,ShowInTaskbar=false,Background=new SolidColorBrush(Color.FromRgb(60,78,92)),Content=new TextBlock{Text="Owned fullscreen-boundary evidence",Foreground=Brushes.White,Margin=new Thickness(12),FontSize=12}};
        var observations=new List<object>();
        void Select(string mode)=>picker.SelectedItem=picker.Items.Cast<ComboBoxItem>().Single(i=>i.Tag?.ToString()==mode);
        bool Surface(string property)=>Field<object>("surfaces").GetType().GetProperty(property)!.GetValue(Field<object>("surfaces")) is true;
        bool Installed()=>Field<object>("statusItem").GetType().GetProperty("Installed")!.GetValue(Field<object>("statusItem")) is true;
        object Snapshot()=>new{visible=window.IsVisible,nativeVisible=ModeBoundaryWindowVisible(new WindowInteropHelper(window).Handle),suppressed=Field<bool>("fullscreenSuppressed"),restore=Field<bool>("restoreAfterFullscreen"),backdrop=Surface("BackdropVisible"),shadow=Surface("ShadowVisible"),mode=engine.Data.Settings.DisplayMode,installed=Installed(),focus=probe.IsActive};
        async Task Menu(bool panel,string background)
        {
            source.Active=false;Set("fullscreenSuppressed",false);Set("fullscreenClearSince",null);Set("restoreAfterFullscreen",false);Set("startupSuppressed",false);Set("suspended",false);
            engine.Data.Settings.Paused=true;engine.Data.Settings.ReduceMotion=true;engine.Data.Settings.BackgroundStyle=background;engine.Data.Settings.WindowX=480;engine.Data.Settings.WindowY=300;engine.ResetObservation();
            Call("ApplyMaterial");Select("floating");Call("ApplyDisplayMode");Select("menuBar");
            if(panel)Call("OpenMenuPanel");
            probe.Show();probe.Activate();await Task.Delay(50);focus.Stop();polling.Stop();
            if(engine.Data.Settings.DisplayMode!="menuBar"||!Installed())throw new Exception("Real menu-mode precondition failed");
        }
        async Task Capture(string name)
        {
            await Task.Delay(60);probe.UpdateLayout();
            if(!probe.IsVisible||!WindowsActivity.GetWindowRect(new WindowInteropHelper(probe).Handle,out var rect))throw new Exception("Own scene not visible");
            using var bitmap=new Drawing.Bitmap(rect.Right-rect.Left,rect.Bottom-rect.Top);
            using(var graphics=Drawing.Graphics.FromImage(bitmap))graphics.CopyFromScreen(rect.Left,rect.Top,0,0,bitmap.Size);
            bitmap.Save(Path.Combine(output,name+".png"),Drawing.Imaging.ImageFormat.Png);
        }
        try
        {
            Call("EnableNativeForeground");Call("EnableNativePulses");
            foreach(var background in new[]{"black","glass"})foreach(var panel in new[]{false,true})
            {
                var label=background+(panel?"-open-panel":"-hidden-panel");await Menu(panel,background);
                var sessions=JsonSerializer.Serialize(engine.Data.Sessions,AtomicStore.JsonOptions);
                source.Active=true;
                check(label+" precondition is newly fullscreen before the next poll",!Field<bool>("fullscreenSuppressed")&&source.Active,null);
                Select("floating");var snap=Snapshot();observations.Add(new{label,snapshot=snap});
                check(label+" late floating selection checks new fullscreen at presentation",!window.IsVisible&&Field<bool>("fullscreenSuppressed"),snap);
                check(label+" owner shadow and backdrop remain natively hidden",!ModeBoundaryWindowVisible(new WindowInteropHelper(window).Handle)&&!Surface("BackdropVisible")&&!Surface("ShadowVisible"),snap);
                check(label+" saves requested mode without changing manual pause or history",store.Load().Settings.DisplayMode=="floating"&&engine.Data.Settings.Paused&&JsonSerializer.Serialize(engine.Data.Sessions,AtomicStore.JsonOptions)==sessions,null);
                await Capture(label+"-blocked");
                source.Active=false;Call("RefreshFullscreenAvoidance");
                check(label+" waits for stable clear rather than showing immediately",!window.IsVisible&&Field<bool>("fullscreenSuppressed"),Snapshot());
                await Task.Delay(1100);Call("RefreshFullscreenAvoidance");await Task.Delay(150);focus.Stop();
                check(label+" clear restores requested floating mode without taking focus",window.IsVisible&&!Field<bool>("fullscreenSuppressed")&&engine.Data.Settings.DisplayMode=="floating"&&!window.IsActive&&probe.IsActive,Snapshot());
            }
            await Menu(false,"black");source.Active=true;Select("floating");Call("HideToTray");source.Active=false;Call("RefreshFullscreenAvoidance");await Task.Delay(1100);Call("RefreshFullscreenAvoidance");
            check("manual hide after the blocked mode choice cancels later restoration",!window.IsVisible&&!Field<bool>("restoreAfterFullscreen"),Snapshot());
            await Menu(false,"black");Call("Persist");var bytes=File.ReadAllBytes(store.Path);source.Active=true;
            using(var held=new FileStream(store.Path,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                Select("floating");
                check("failed late mode save retains stored menu mode and selector",engine.Data.Settings.DisplayMode=="menuBar"&&((ComboBoxItem)picker.SelectedItem).Tag?.ToString()=="menuBar"&&Installed(),Snapshot());
                check("failed late mode save preserves primary bytes",File.ReadAllBytes(store.Path).SequenceEqual(bytes),null);
            }
            File.WriteAllText(Path.Combine(output,"mode-fullscreen-boundary.json"),JsonSerializer.Serialize(new{observations,scope="Controlled new-fullscreen signal with polling stopped, actual WPF ComboBox changes and native owner/shadow/backdrop visibility, own focus scene screen capture. Not a real game or physical notification-icon click."},AtomicStore.JsonOptions));
        }
        finally
        {
            source.Active=false;Set("fullscreenSuppressed",false);Set("fullscreenClearSince",null);Set("restoreAfterFullscreen",false);Set("suspended",false);Set("startupSuppressed",false);
            var saved=JsonSerializer.Deserialize<AppState>(original,AtomicStore.JsonOptions)!;engine.Data.Settings=saved.Settings;engine.Data.Sessions.Clear();engine.Data.Sessions.AddRange(saved.Sessions);engine.ResetObservation();
            Call("InitializeVisualPreferences");Call("ApplyDisplayMode");Call("ApplyMaterial");Call("Persist");probe.Close();focus.Stop();await Task.Delay(100);
        }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="IsWindowVisible")]
    private static extern bool ModeBoundaryWindowVisible(nint window);
}
