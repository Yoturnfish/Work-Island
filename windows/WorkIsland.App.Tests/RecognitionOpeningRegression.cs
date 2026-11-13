using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunRecognitionOpeningChecks(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        void Set(string name,object value)=>typeof(IslandWindow).GetField(name,Flags)!.SetValue(window,value);
        object? Call(string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        var settings=Field<TrackingEngine>("engine").Data.Settings;settings.Paused=true;settings.HasCompletedOnboarding=true;
        var source=Field<FullscreenFixture>("fullscreenSource");var picker=Field<ApplicationPickerFixture>("applicationPicker");
        Field<DispatcherTimer>("fullscreenTimer").Stop();Field<DispatcherTimer>("focusReturn").Stop();
        void OpenPage(){if(!Field<bool>("isExpanded"))Call("ToggleExpanded",null,new RoutedEventArgs());Call("OpenRecognition",null,new RoutedEventArgs());}
        void Choose()=>Call("ChooseApplication",Field<Button>("AddApplicationSelectionButton"),new RoutedEventArgs());
        async Task Auto()=>await (Task)Call("OpenRecognitionPicker",Field<Button>("AutomaticRecognitionButton"),"all")!;
        bool Open()=>typeof(IslandWindow).GetField("recognitionPicker",Flags)!.GetValue(window) is Popup{IsOpen:true};
        async Task Clear(){source.Active=false;Call("RefreshFullscreenAvoidance");await Task.Delay(1100);Call("RefreshFullscreenAvoidance");OpenPage();await Task.Delay(270);Field<DispatcherTimer>("focusReturn").Stop();}
        async Task Blocked(string label)
        {
            var before=picker.Calls;Choose();
            check(label+": file chooser is not invoked",picker.Calls==before,new{before,after=picker.Calls});
            await Auto();
            check(label+": recognition popup is not opened",!Open(),new{visible=window.IsVisible,suppressed=Field<bool>("fullscreenSuppressed")});
            Call("CloseRecognitionPicker");Field<DispatcherTimer>("focusReturn").Stop();
        }
        OpenPage();await Task.Delay(300);
        try
        {
            // Arrives between poll ticks: the product's entry must query again.
            source.Active=true;var before=picker.Calls;Choose();
            check("new fullscreen is checked at the native chooser opening boundary",picker.Calls==before&&Field<bool>("fullscreenSuppressed")&&!window.IsVisible,new{before,after=picker.Calls,suppressed=Field<bool>("fullscreenSuppressed"),window.IsVisible});
            Call("RefreshFullscreenAvoidance");await Blocked("already suppressed");
            source.Active=false;Call("RefreshFullscreenAvoidance");await Blocked("clear debounce");
            await Clear();before=picker.Calls;Choose();
            check("stable clear permits one normal chooser request",picker.Calls==before+1&&!Field<bool>("applicationChooserOpen"),new{before,after=picker.Calls});
            source.Active=true;await Auto();
            check("new fullscreen is checked at recognition popup opening boundary",!Open()&&Field<bool>("fullscreenSuppressed")&&!window.IsVisible,null);
            Call("CloseRecognitionPicker");await Clear();
            Set("suspended",true);await Blocked("session suspended");Set("suspended",false);
            Call("HideIsland",null,new RoutedEventArgs());await Blocked("manually hidden");Call("ShowIsland");
            await Task.Delay(100);await Auto();
            check("visible clear window can reopen the actual recognition popup",Open(),null);
            Call("CloseRecognitionPicker");
            check("blocked opening does not alter rules websites or paused state",settings.Rules.Count==0&&settings.Websites.Count==0&&settings.Paused,null);
            File.WriteAllText(Path.Combine(output,"opening-scope.json"),JsonSerializer.Serialize(new{scope="Actual WPF Popup and controlled FullscreenFixture, native-file-picker invocation counter. Direct entry requests model queued actions around poll boundaries. No real game, physical click, actual system session or native dialog selection."},AtomicStore.JsonOptions));
        }
        finally
        {
            source.Active=false;Set("suspended",false);Set("fullscreenSuppressed",false);Call("CloseRecognitionPicker");window.ShowActivated=false;window.Show();Call("CollapseToTimer");Field<DispatcherTimer>("focusReturn").Stop();
        }
    }
}
