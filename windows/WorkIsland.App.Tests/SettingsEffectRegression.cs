using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunSettingsEffectChecks(IslandWindow window,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|System.Reflection.BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var store=Field<AtomicStore>("store");
        var saved=JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions);
        void Select(string name,string tag)
        {
            var picker=Field<ComboBox>(name);picker.SelectedItem=picker.Items.Cast<ComboBoxItem>().Single(i=>i.Tag?.ToString()==tag);
        }
        try
        {
            Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenSettings",window,new RoutedEventArgs());await Task.Delay(300);
            Select("GoalPicker","2");
            check("goal setting changes tracking denominator and saved value",engine.Data.Settings.GoalSeconds==7200&&store.Load().Settings.GoalSeconds==7200,null);
            Select("IdlePicker","60");
            engine.Data.Settings.Rules=[new("settings-test.exe","Settings")];
            engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new("settings-test.exe","Settings",45));
            check("idle selection affects actual decision threshold",engine.Current.State=="tracking"&&store.Load().Settings.AwayThreshold==60,null);
            Select("IdlePicker","30");engine.Observe(DateTimeOffset.Now,new("settings-test.exe","Settings",45));
            check("shorter selected threshold changes tracking to away",engine.Current.State=="away",null);
            Select("DurationPicker","minutesSeconds");Call("Render");
            check("time-format setting changes rendered timer and saved format",Field<TextBlock>("TimeLabel").Text.Contains('S')&&store.Load().Settings.DurationStyle=="minutesSeconds",null);
            Select("AwayPicker","true");typeof(IslandWindow).GetField("awaySince",Flags)!.SetValue(window,(DateTimeOffset?)DateTimeOffset.Now.AddSeconds(-62));
            var before=engine.TodaySeconds(DateTimeOffset.Now);Call("Render");
            check("away timer renders elapsed away duration without work credit",Field<TextBlock>("TimeLabel").Text.StartsWith("01M 02")&&engine.TodaySeconds(DateTimeOffset.Now)==before&&store.Load().Settings.AwayTimerEnabled,null);
            Select("AwayPicker","false");Call("Render");
            check("turning off away timer restores accumulated work display",!Field<TextBlock>("TimeLabel").Text.StartsWith("01M 02")&&engine.TodaySeconds(DateTimeOffset.Now)==before,null);
            Select("ReminderPicker","true");
            check("reminder setting starts actual glow clock",Field<FrameworkElement>("ReminderGlow").HasAnimatedProperties&&store.Load().Settings.SuperReminderEnabled,null);
            Select("ReminderPicker","false");
            check("reminder off releases glow clock immediately",!Field<FrameworkElement>("ReminderGlow").HasAnimatedProperties,null);
            Select("BackgroundPicker","glass");
            check("transparent background changes material and removes opacity control",engine.Data.Settings.BackgroundStyle=="glass"&&window.Opacity==1&&Field<FrameworkElement>("OpacityRow").Visibility==Visibility.Collapsed&&store.Load().Settings.BackgroundStyle=="glass",null);
            Select("BackgroundPicker","black");Call("CollapseToTimer");await Task.Delay(300);Call("NotifyPointer",false);
            Field<Slider>("OpacitySlider").Value=.65;
            check("opacity slider affects native window and persistence",Math.Abs(window.Opacity-.65)<.001&&store.Load().Settings.BackgroundOpacity==.65,null);
            Select("LanguagePicker","en");
            var display=Field<ComboBox>("DisplayPicker");
            check("language setting translates live controls and both modes",Field<TextBlock>("TitleLabel").Text=="Screen work time"&&display.Items.Cast<ComboBoxItem>().Single(i=>i.Tag?.ToString()=="menuBar").Content?.ToString()=="Menu bar"&&store.Load().Settings.Language=="en",null);
            var login=Field<CheckBox>("LoginCheck");login.IsChecked=true;login.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            check("login setting invokes isolated registration provider",Field<IStartupRegistration>("startupRegistration").IsEnabled,null);
            login.IsChecked=false;login.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            check("login off removes isolated registration",!Field<IStartupRegistration>("startupRegistration").IsEnabled,null);
        }
        finally
        {
            var restored=JsonSerializer.Deserialize<AppState>(saved,AtomicStore.JsonOptions)!;engine.Data.Settings=restored.Settings;
            engine.Data.Sessions.Clear();engine.Data.Sessions.AddRange(restored.Sessions);engine.ResetObservation();
            typeof(IslandWindow).GetField("loadingPreferences",Flags)!.SetValue(window,true);
            Select("DisplayPicker",restored.Settings.DisplayMode);Select("GoalPicker",(restored.Settings.GoalSeconds/3600).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Select("IdlePicker",restored.Settings.AwayThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture));Select("DurationPicker",restored.Settings.DurationStyle);
            Select("AwayPicker",restored.Settings.AwayTimerEnabled?"true":"false");Select("ReminderPicker",restored.Settings.SuperReminderEnabled?"true":"false");
            Select("BackgroundPicker",restored.Settings.BackgroundStyle);Select("LanguagePicker",restored.Settings.Language);Field<Slider>("OpacitySlider").Value=restored.Settings.BackgroundOpacity;
            typeof(IslandWindow).GetField("loadingPreferences",Flags)!.SetValue(window,false);
            Call("LoadSettingsInputs");Call("ApplyMaterial");Call("ProfileChanged");await Task.Delay(400);
        }
    }
}
