using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static Task RunNumericPreferenceChecks(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");var store=Field<AtomicStore>("store");
        var original=JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions);var oldCulture=CultureInfo.CurrentCulture;
        var observations=new List<object>();
        void Prepare(double goalSeconds,double idle)
        {
            engine.Data.Settings.Paused=true;engine.Data.Settings.GoalSeconds=goalSeconds;engine.Data.Settings.AwayThreshold=idle;
            engine.ResetObservation();Call("InitializeVisualPreferences");Call("LoadSettingsInputs");Call("Persist");
        }
        void Select(string name,string tag)=>Field<ComboBox>(name).SelectedItem=Field<ComboBox>(name).Items.Cast<ComboBoxItem>().Single(i=>i.Tag?.ToString()==tag);
        void Observe(string label,string picker,string tag,double goal,double idle)
        {
            var before=JsonSerializer.Serialize(engine.Data.Sessions,AtomicStore.JsonOptions);
            Select(picker,tag);var disk=store.Load();var selected=((ComboBoxItem?)Field<ComboBox>(picker).SelectedItem)?.Tag?.ToString();
            var item=new{culture=CultureInfo.CurrentCulture.Name,label,picker,tag,selected,expectedGoal=goal,expectedIdle=idle,actualGoal=engine.Data.Settings.GoalSeconds,actualIdle=engine.Data.Settings.AwayThreshold,diskGoal=disk.Settings.GoalSeconds,diskIdle=disk.Settings.AwayThreshold};observations.Add(item);
            check(label+" applies only the selected numeric value",engine.Data.Settings.GoalSeconds==goal&&engine.Data.Settings.AwayThreshold==idle,item);
            check(label+" persists exact pair and keeps selector aligned",disk.Settings.GoalSeconds==goal&&disk.Settings.AwayThreshold==idle&&selected==tag,item);
            check(label+" preserves paused state and historical records",engine.Data.Settings.Paused&&disk.Settings.Paused&&engine.Current.State=="paused"&&JsonSerializer.Serialize(engine.Data.Sessions,AtomicStore.JsonOptions)==before,null);
        }
        try
        {
            engine.Data.Settings.ReduceMotion=true;
            foreach(var culture in new[]{"en-US","de-DE","fr-FR"})
            {
                CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(culture);
                // A valid persisted custom value creates a selectable custom
                // item through the real startup preference initialization.
                Prepare(5400,12.5);
                Prepare(5401,30.25);Observe(culture+" idle edit retains precise goal","IdlePicker","60",5401,60);
                Prepare(5401,30.25);Observe(culture+" goal edit retains fractional threshold","GoalPicker","2",7200,30.25);
                Prepare(7200,30);Observe(culture+" returning to custom 1.5h","GoalPicker","1.5",5400,30);
                Prepare(7200,30);Observe(culture+" returning to custom 12.5s","IdlePicker","12.5",7200,12.5);
                Prepare(7200,30.25);var bytes=File.ReadAllBytes(store.Path);
                using(var held=new FileStream(store.Path,FileMode.Open,FileAccess.Read,FileShare.Read))
                {
                    Select("GoalPicker","1.5");
                    check(culture+" rejected numeric choice restores exact prior pair",engine.Data.Settings.GoalSeconds==7200&&engine.Data.Settings.AwayThreshold==30.25,new{engine.Data.Settings.GoalSeconds,engine.Data.Settings.AwayThreshold});
                    check(culture+" rejected numeric choice preserves bytes and selector",File.ReadAllBytes(store.Path).SequenceEqual(bytes)&&((ComboBoxItem)Field<ComboBox>("GoalPicker").SelectedItem).Tag?.ToString()=="2",null);
                }
                Call("Persist");check(culture+" recovery does not commit a rejected numeric choice",store.Load().Settings.GoalSeconds==7200&&store.Load().Settings.AwayThreshold==30.25,null);
                Select("GoalPicker","2");Observe(culture+" numeric retry after real lock","GoalPicker","1.5",5400,30.25);
            }
            File.WriteAllText(Path.Combine(output,"numeric-preferences.json"),JsonSerializer.Serialize(new{observations,scope="Real WPF selection events and file locks, valid persisted custom values, per-process CurrentCulture only; no Windows locale or user preview edits. Mac numeric bindings are the behavior reference."},AtomicStore.JsonOptions));
        }
        finally
        {
            CultureInfo.CurrentCulture=oldCulture;
            var saved=JsonSerializer.Deserialize<AppState>(original,AtomicStore.JsonOptions)!;
            engine.Data.Settings=saved.Settings;engine.Data.Sessions.Clear();engine.Data.Sessions.AddRange(saved.Sessions);engine.ResetObservation();
            Call("InitializeVisualPreferences");Call("LoadSettingsInputs");Call("Persist");Call("Render");
        }
        return Task.CompletedTask;
    }
}
