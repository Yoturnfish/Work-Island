using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunPreferenceSaveChecks(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var engine=Field<TrackingEngine>("engine");var store=Field<AtomicStore>("store");var settings=engine.Data.Settings;
        void Select(string name,string tag){var picker=Field<ComboBox>(name);picker.SelectedItem=picker.Items.Cast<ComboBoxItem>().Single(i=>i.Tag?.ToString()==tag);}
        string? Selection(string name)=>(Field<ComboBox>(name).SelectedItem as ComboBoxItem)?.Tag?.ToString();
        bool Error(){var label=Field<TextBlock>("MessageLabel");return label.IsVisible&&(label.Text.Contains("保存失败")||label.Text.Contains("Save failed"));}
        void Settings(){if(!Field<bool>("isExpanded"))Call("ToggleExpanded",null,new RoutedEventArgs());Call("ShowPage","settings",false);Field<System.Windows.Threading.DispatcherTimer>("focusReturn").Stop();}
        settings.Paused=true;settings.Rules=[];settings.ReduceMotion=false;settings.BackgroundStyle="black";settings.BackgroundOpacity=1;settings.SuperReminderEnabled=false;settings.AwayTimerEnabled=false;settings.GoalSeconds=14400;settings.AwayThreshold=30;settings.DurationStyle="hoursMinutes";settings.Language="zh";
        engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new(null,null,0));Call("LoadSettingsInputs");Settings();Call("Render");await Task.Delay(300);
        var cases=new (string picker,string from,string to,Func<string> value)[]{
            ("BackgroundPicker","black","glass",()=>settings.BackgroundStyle),("DurationPicker","hoursMinutes","minutesSeconds",()=>settings.DurationStyle),
            ("GoalPicker","4","2",()=>(settings.GoalSeconds/3600).ToString(CultureInfo.InvariantCulture)),("IdlePicker","30","60",()=>settings.AwayThreshold.ToString(CultureInfo.InvariantCulture)),
            ("AwayPicker","false","true",()=>settings.AwayTimerEnabled?"true":"false"),("ReminderPicker","false","true",()=>settings.SuperReminderEnabled?"true":"false"),("LanguagePicker","zh","en",()=>settings.Language)};
        var observations=new List<object>();
        foreach(var item in cases)
        {
            Select(item.picker,item.from);engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new(null,null,0));Settings();Call("Persist");var original=File.ReadAllBytes(store.Path);
            using(var locked=new FileStream(store.Path,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                Select(item.picker,item.to);await Task.Delay(40);
                var snapshot=new{item.picker,model=item.value(),selection=Selection(item.picker),message=Field<TextBlock>("MessageLabel").Text,engine.Current.State};observations.Add(snapshot);
                check(item.picker+" failed save restores applied setting",item.value()==item.from,snapshot);
                check(item.picker+" failed save restores visible selector",Selection(item.picker)==item.from,snapshot);
                check(item.picker+" failure stays on settings and cannot become success feedback",Error()&&ReferenceEquals(Field<FrameworkElement>("activePage"),Field<FrameworkElement>("SettingsPage")),snapshot);
                check(item.picker+" failed save cannot alter primary bytes",File.ReadAllBytes(store.Path).SequenceEqual(original),null);
                if(item.picker is "GoalPicker" or "IdlePicker")check(item.picker+" failed edit preserves paused tracking decision",engine.Current.State=="paused",snapshot);
                if(item.picker=="ReminderPicker")check("rejected Flash selection cannot start an unsaved preview",Field<AnimationClock?>("reminderClock")==null&&!Field<FrameworkElement>("ReminderGlow").HasAnimatedProperties,null);
                if(item.picker=="BackgroundPicker")check("rejected glass does not alter visible material controls",Field<FrameworkElement>("OpacityRow").Visibility==Visibility.Visible,null);
                if(item.picker=="LanguagePicker")check("rejected language keeps original live labels",Field<TextBlock>("TitleLabel").Text=="屏幕工作时间",null);
                if(item.picker is "GoalPicker" or "IdlePicker")check(item.picker+" failure restores backing input text",item.picker=="GoalPicker"?Field<TextBox>("GoalInput").Text=="4":Field<TextBox>("IdleInput").Text=="30",null);
            }
            Call("Persist");var recovered=store.Load().Settings;var property=item.picker switch{"BackgroundPicker"=>nameof(settings.BackgroundStyle),"DurationPicker"=>nameof(settings.DurationStyle),"GoalPicker"=>nameof(settings.GoalSeconds),"IdlePicker"=>nameof(settings.AwayThreshold),"AwayPicker"=>nameof(settings.AwayTimerEnabled),"ReminderPicker"=>nameof(settings.SuperReminderEnabled),_=>nameof(settings.Language)};
            var expected=item.picker=="GoalPicker"?"14400":item.from;var value=typeof(Settings).GetProperty(property)!.GetValue(recovered);var persisted=value is bool boolean?boolean?"true":"false":Convert.ToString(value,CultureInfo.InvariantCulture);
            check(item.picker+" background recovery cannot commit a rejected setting",persisted==expected,new{persisted,expected});
            Select(item.picker,item.from);Select(item.picker,item.to);
            var retry=store.Load().Settings;var retryValue=typeof(Settings).GetProperty(property)!.GetValue(retry);var actual=retryValue is bool retryBoolean?retryBoolean?"true":"false":Convert.ToString(retryValue,CultureInfo.InvariantCulture);var retryExpected=item.picker=="GoalPicker"?"7200":item.to;
            check(item.picker+" explicit retry durably applies selected value",item.value()==item.to&&Selection(item.picker)==item.to&&actual==retryExpected,null);
            check(item.picker+" successful retry clears the previous setting error",!Error(),Field<TextBlock>("MessageLabel").Text);
            Select(item.picker,item.from);
        }
        Settings();Call("NotifyPointer",false);var slider=Field<Slider>("OpacitySlider");slider.Value=1;Call("Persist");var beforeOpacity=File.ReadAllBytes(store.Path);
        using(var locked=new FileStream(store.Path,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            slider.Value=.65;await Task.Delay(40);
            check("opacity save failure restores setting and slider",settings.BackgroundOpacity==1&&slider.Value==1,new{settings.BackgroundOpacity,slider.Value});
            check("opacity save failure keeps actual settings opacity",Math.Abs(window.Opacity-1)<.001&&Error(),new{window.Opacity});
            check("opacity save failure preserves primary bytes",File.ReadAllBytes(store.Path).SequenceEqual(beforeOpacity),null);
        }
        Call("Persist");check("opacity background recovery cannot commit rejected transparency",store.Load().Settings.BackgroundOpacity==1,null);
        slider.Value=1;slider.Value=.65;check("opacity explicit retry saves actual selected opacity",settings.BackgroundOpacity==.65&&store.Load().Settings.BackgroundOpacity==.65,null);
        slider.Value=1;Select("ReminderPicker","true");var preview=Field<AnimationClock?>("reminderClock");Call("Persist");
        using(var locked=new FileStream(store.Path,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            Select("ReminderPicker","false");
            check("failed Off retains saved Flash selection and its existing preview",settings.SuperReminderEnabled&&Selection("ReminderPicker")=="true"&&ReferenceEquals(preview,Field<AnimationClock?>("reminderClock")),null);
        }
        Select("ReminderPicker","true");Select("ReminderPicker","false");
        var pending=new Session{ApplicationIdentifier="pending-preference.exe",ApplicationName="Pending work",StartedAt=DateTimeOffset.Now.AddSeconds(-10),EndedAt=DateTimeOffset.Now.AddSeconds(-8),EffectiveSeconds=2,DayKey=LogicalDay.Key(DateTimeOffset.Now,engine.Zone,settings.DayResetMinutes)};
        Call("Persist");engine.Data.Sessions.Add(pending);
        using(var locked=new FileStream(store.Path,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            Select("GoalPicker","2");
            check("failed preference retains pending work identity timestamps and total",engine.Data.Sessions.Contains(pending)&&pending.EffectiveSeconds==2&&settings.GoalSeconds==14400,null);
        }
        Call("Persist");var recoveredPending=store.Load();
        check("background recovery saves pending work with the original goal",recoveredPending.Settings.GoalSeconds==14400&&recoveredPending.Sessions.Any(s=>s.Id==pending.Id&&s.EffectiveSeconds==2&&s.StartedAt==pending.StartedAt&&s.EndedAt==pending.EndedAt),null);
        Select("GoalPicker","4");Field<TextBox>("GoalInput").Text="2";Field<TextBox>("IdleInput").Text="NaN";var beforeInvalid=File.ReadAllBytes(store.Path);Call("SaveSettings",null,new RoutedEventArgs());
        check("invalid combined legacy settings cannot partially apply goal or write storage",settings.GoalSeconds==14400&&settings.AwayThreshold==30&&File.ReadAllBytes(store.Path).SequenceEqual(beforeInvalid),null);
        Call("LoadSettingsInputs");
        Select("LanguagePicker","en");
        using(var locked=new FileStream(store.Path,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            Select("GoalPicker","2");
            check("English setting rejection remains concise English failure feedback",Error()&&Field<TextBlock>("MessageLabel").Text.Contains("Unchanged")&&!Field<TextBlock>("MessageLabel").Text.Contains("已保存"),Field<TextBlock>("MessageLabel").Text);
        }
        Select("GoalPicker","4");Select("LanguagePicker","zh");
        File.WriteAllText(Path.Combine(output,"preference-save.json"),JsonSerializer.Serialize(new{productDllSha256=RuntimeCompat.ToHexString(RuntimeCompat.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),observations,scope="Actual primary-file locks, actual WPF selection and slider events, controlled paused/isolated data. Failure is a real IO rejection; not native input, physical OS session or game acceptance."},AtomicStore.JsonOptions));
    }
}

