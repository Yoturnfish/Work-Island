using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunPickerFocusChecks(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var timer=Field<DispatcherTimer>("focusReturn");var observations=new List<object>();
        window.Left=800;window.Top=260;
        foreach(var name in new[]{"DisplayPicker","BackgroundPicker","DurationPicker","GoalPicker","IdlePicker","AwayPicker","ReminderPicker","LanguagePicker"})
        {
            Call("CollapseToTimer");await Task.Delay(300);Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenSettings",window,new RoutedEventArgs());await Task.Delay(300);
            var picker=Field<ComboBox>(name);picker.BringIntoView();window.UpdateLayout();picker.IsDropDownOpen=true;await Task.Delay(80);
            var popup=(Popup)picker.Template.FindName("PART_Popup",picker)!;var held=(bool)Call("HasOpenPicker")!;
            if(name=="DisplayPicker")Call("SaveScreenRegion",Path.Combine(output,"display-open.png"),23);
            check($"{name} real dropdown is visible and protected from focus return",picker.IsDropDownOpen&&popup.IsOpen&&held,new{picker.IsDropDownOpen,popup.IsOpen,held});
            // Model a focus-return callback that was already pending when the
            // dropdown became open. This starts the real dispatcher timer;
            // it is not a native Deactivated/input acceptance claim.
            timer.Start();await Task.Delay(1250);
            var expanded=Field<bool>("isExpanded");var onSettings=Field<FrameworkElement>("SettingsPage").IsVisible;
            if(name=="DisplayPicker")Call("SaveScreenRegion",Path.Combine(output,"display-after-focus-timer.png"),23);
            observations.Add(new{name,held,expanded,onSettings,open=picker.IsDropDownOpen,popupOpen=popup.IsOpen});
            check($"{name} pending focus timer keeps the actual settings page open",expanded&&onSettings&&picker.IsDropDownOpen,new{expanded,onSettings,picker.IsDropDownOpen});
            var fullscreen=Field<FullscreenFixture>("fullscreenSource");fullscreen.Active=true;Call("RefreshFullscreenAvoidance");await Task.Delay(60);
            check($"{name} fullscreen avoidance closes the actual dropdown and hides its owner",!picker.IsDropDownOpen&&!popup.IsOpen&&!window.IsVisible,null);
            fullscreen.Active=false;Call("RefreshFullscreenAvoidance");await Task.Delay(1100);Call("RefreshFullscreenAvoidance");await Task.Delay(60);
            check($"{name} fullscreen clear restores owner without reopening dropdown",window.IsVisible&&!picker.IsDropDownOpen&&!popup.IsOpen,null);
            picker.IsDropDownOpen=false;timer.Stop();await Task.Delay(50);
        }
        File.WriteAllText(Path.Combine(output,"picker-focus.json"),JsonSerializer.Serialize(new{observations,scope="Actual isolated WPF ComboBox popups and dispatcher timer, controlled pending-focus condition. No hardware input or external-window focus acceptance implied."},AtomicStore.JsonOptions));
    }
}
