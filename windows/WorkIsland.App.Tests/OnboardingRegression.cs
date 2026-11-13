using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunOnboardingChecks(string output,Action<string,bool,object?> check)
    {
        var folder=Path.Combine(output,"onboarding");Directory.CreateDirectory(folder);
        var file=Path.Combine(folder,"state.json");var store=new AtomicStore(file);var state=store.Load();state.Settings.Language="zh";state.Settings.Rules=[];
        var source=new FullscreenFixture();var window=new IslandWindow(store,state,false,null,startupRegistration:new IsolatedStartupRegistration(Path.Combine(folder,"startup.json"),"isolated"),recognitionSource:new RecognitionFixture(),applicationPicker:new ApplicationPickerFixture(),fullscreenSource:source);
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] arguments)=>typeof(IslandWindow).GetMethod(name,Flags)!.Invoke(window,arguments);
        void Move(int direction)=>Call("MoveOnboarding",direction);
        bool Active()=>(bool)typeof(IslandWindow).GetProperty("OnboardingActive",Flags)!.GetValue(window)!;
        void Capture(string name)=>Call("SaveImage2x",Path.Combine(folder,name+"-2x.png"));
        var samples=new List<object>();var watch=Stopwatch.StartNew();
        void Sample(string phase)=>samples.Add(new{phase,seconds=watch.Elapsed.TotalSeconds,step=Field<int>("onboardingStep"),alpha=Field<Grid[]>("onboardingSteps").Select(s=>s.Opacity).ToArray(),x=Field<Grid[]>("onboardingSteps").Select(s=>((TranslateTransform)s.RenderTransform).X).ToArray()});
        var timer=Field<DispatcherTimer>("timer");window.ShowActivated=false;window.Topmost=false;window.Loaded+=(_,_)=>timer.Stop();
        try
        {
            window.Show();await Task.Delay(80);
            var enteringAlpha=Field<Grid>("onboardingRoot").Opacity;Move(1);
            check("early next step preserves the in-progress onboarding entry fade",typeof(IslandWindow).GetField("onboardingRootMotion",Flags)!.GetValue(window) is not null&&Math.Abs(Field<Grid>("onboardingRoot").Opacity-enteringAlpha)<.06,new{before=enteringAlpha,after=Field<Grid>("onboardingRoot").Opacity});
            await Task.Delay(80);Move(-1);await Task.Delay(400);
            var pixel=1/VisualTreeHelper.GetDpi(window).PixelsPerDip;
            check("new installation automatically shows only onboarding on the existing compact canvas",Active()&&!state.Settings.HasCompletedOnboarding&&Field<Grid>("CompactPanel").Visibility==Visibility.Collapsed&&Field<Grid>("onboardingRoot").Width==344&&Field<Grid>("onboardingRoot").Height==92&&Math.Abs(window.Width-344*state.Settings.IslandScale)<pixel&&Math.Abs(window.Height-92*state.Settings.IslandScale)<pixel,new{active=Active(),completed=state.Settings.HasCompletedOnboarding,compact=Field<Grid>("CompactPanel").Visibility.ToString(),window.Width,window.Height,scale=state.Settings.IslandScale,physicalPixelInDip=pixel});Capture("01-language");
            Call("BeginFocusReturn");check("onboarding does not schedule focus loss collapse",!Field<DispatcherTimer>("focusReturn").IsEnabled,null);
            var back=Field<Button>("onboardingBack");check("first step hides and disables back navigation",!back.IsEnabled&&back.Opacity==0,null);
            var languages=Field<List<(Button Button,string Language)>>("onboardingLanguages");languages.Single(p=>p.Language=="en").Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check("language changes immediately update the live setting and persist",state.Settings.Language=="en"&&store.Load().Settings.Language=="en",null);Capture("01-language-english");
            languages.Single(p=>p.Language=="zh").Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Move(1);await Task.Delay(80);Sample("goal intermediate");var goalStep=Field<Grid[]>("onboardingSteps")[1];check("onboarding step uses a real intermediate slide and fade",goalStep.Opacity>0&&goalStep.Opacity<1&&Math.Abs(((TranslateTransform)goalStep.RenderTransform).X)>1,null);Capture("02-goal-midpoint");
            var goalClock=Field<ClockGroup>("onboardingMotion");check("step transition matches Mac .24 seconds",goalClock.Timeline.Duration.TimeSpan==TimeSpan.FromSeconds(.24),null);
            var oldX=((TranslateTransform)goalStep.RenderTransform).X;var oldAlpha=goalStep.Opacity;Move(-1);Sample("reverse immediate");check("rapid reverse starts from the current presented position and opacity",Math.Abs(((TranslateTransform)goalStep.RenderTransform).X-oldX)<2&&Math.Abs(goalStep.Opacity-oldAlpha)<.05,null);
            await Task.Delay(40);Move(1);await Task.Delay(300);Sample("goal settled");check("rapid second reverse settles only the intended step and releases the clock",Field<int>("onboardingStep")==1&&Field<Grid[]>("onboardingSteps").Count(s=>s.Visibility==Visibility.Visible)==1&&typeof(IslandWindow).GetField("onboardingMotion",Flags)!.GetValue(window) is null,null);
            var goal=Field<ComboBox>("onboardingGoal");
            check("new installation presents and persists the Mac 2h initial goal",state.Settings.GoalSeconds==7200&&Equals((goal.SelectedItem as ComboBoxItem)?.Tag,7200d)&&store.Load().Settings.GoalSeconds==7200,new{state.Settings.GoalSeconds,selection=(goal.SelectedItem as ComboBoxItem)?.Tag});Capture("02-initial-goal");
            goal.SelectedItem=goal.Items.Cast<ComboBoxItem>().Single(i=>Equals(i.Tag,21600d));check("onboarding goal updates actual tracking settings and saved data",state.Settings.GoalSeconds==21600&&store.Load().Settings.GoalSeconds==21600,null);Capture("02-goal");
            Move(1);await Task.Delay(290);Capture("03-recognition");var auto=Field<Grid[]>("onboardingSteps")[2].Children.OfType<Button>().Single();auto.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(140);
            var popup=Field<Popup>("recognitionPicker");check("onboarding recognition opens the shared running application and website picker",popup.IsOpen&&Active()&&Field<int>("onboardingStep")==2,null);
            Call("ToggleRecognizedApplication",new RunningApplication("fixture-running.exe","Running fixture",0));Call("ToggleRecognizedWebsite",new BrowserPage("msedge.exe","Edge",0,"https://example.com/path?private=fixture",null));
            // Automatic capture follows the Mac primary-domain keyword rule;
            // explicit/manual example.com rules intentionally retain full host.
            check("shared onboarding recognition adds real rules without navigating away",state.Settings.Rules.Any(r=>r.Identifier=="fixture-running.exe")&&state.Settings.Websites.Any(r=>r.PathContains=="example"&&r.TitleContains=="example"&&r.DisplayName=="example"&&r.IsEnabled)&&Field<int>("onboardingStep")==2,new{step=Field<int>("onboardingStep"),websites=state.Settings.Websites.Select(r=>new{r.Identifier,r.DisplayName,r.PathContains,r.TitleContains,r.IsEnabled}).ToArray()});
            Move(1);check("leaving recognition closes the picker and invalidates stale results",!popup.IsOpen,null);await Task.Delay(290);Capture("04-tutorial");
            source.Active=true;Call("RefreshFullscreenAvoidance");check("fullscreen hides onboarding and settles its motion",!window.IsVisible&&Active()&&typeof(IslandWindow).GetField("onboardingMotion",Flags)!.GetValue(window) is null,null);
            source.Active=false;Call("RefreshFullscreenAvoidance");await Task.Delay(1100);Call("RefreshFullscreenAvoidance");check("cleared fullscreen restores the same onboarding step",window.IsVisible&&Active()&&Field<int>("onboardingStep")==3,null);
            state.Settings.ReduceMotion=true;Move(-1);check("reduced motion switches steps immediately with no clock",Field<int>("onboardingStep")==2&&typeof(IslandWindow).GetField("onboardingMotion",Flags)!.GetValue(window) is null,null);state.Settings.ReduceMotion=false;Move(1);Move(1);await Task.Delay(290);Capture("05-complete");
            var held=folder+".held";var scope=Path.GetFullPath(output)+Path.DirectorySeparatorChar;if(!Path.GetFullPath(folder).StartsWith(scope,StringComparison.OrdinalIgnoreCase)||!Path.GetFullPath(held).StartsWith(scope,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Fixture outside output scope");
            Directory.Move(folder,held);File.WriteAllText(folder,"Owned fixture blocks onboarding completion save.");Call("FinishOnboarding");check("failed completion save keeps onboarding and its incomplete flag",Active()&&!state.Settings.HasCompletedOnboarding&&Field<TextBlock>("onboardingStatus").Text.Contains("保存失败"),null);
            File.Move(folder,folder+".blocker-kept");Directory.Move(held,folder);Call("FinishOnboarding");await Task.Delay(100);check("floating completion has Mac .38 second scale and opacity overlap",!Active()&&Field<Grid>("onboardingRoot").Opacity>0&&Field<Grid>("onboardingRoot").Opacity<1&&((ScaleTransform)Field<Grid>("onboardingRoot").RenderTransform).ScaleX>1&&((ScaleTransform)Field<Grid>("CompactPanel").RenderTransform).ScaleX<1,null);Capture("06-finish-midpoint");
            await Task.Delay(350);check("completion persists and returns to the unchanged compact screen",store.Load().Settings.HasCompletedOnboarding&&!Active()&&Field<Grid>("onboardingRoot").Visibility==Visibility.Collapsed&&Field<Grid>("CompactPanel").Visibility==Visibility.Visible&&typeof(IslandWindow).GetField("onboardingRootMotion",Flags)!.GetValue(window) is null,null);Capture("06-finished");
            Call("ChangeDisplayMode","menuBar");var priorPosition=new Point(window.Left,window.Top);var savedX=state.Settings.WindowX;var savedY=state.Settings.WindowY;Call("BeginOnboarding");await Task.Delay(430);
            check("menu bar onboarding temporarily floats without changing the saved mode",Active()&&window.IsVisible&&state.Settings.DisplayMode=="menuBar"&&!Field<bool>("menuPanelOpen"),null);
            state.Settings.ReduceMotion=true;for(var i=0;i<4;i++)Move(1);Call("FinishOnboarding");check("completion restores menu mode position and original floating coordinates",!Active()&&state.Settings.DisplayMode=="menuBar"&&!window.IsVisible&&Math.Abs(window.Left-priorPosition.X)<.1&&Math.Abs(window.Top-priorPosition.Y)<.1&&state.Settings.WindowX==savedX&&state.Settings.WindowY==savedY,null);
            File.WriteAllText(Path.Combine(folder,"motion-samples.json"),JsonSerializer.Serialize(new{stepDurationSeconds=.24,rootDurationSeconds=.38,samples,limitation="Actual WPF animated properties and isolated fixtures; not physical game or system-session acceptance."},AtomicStore.JsonOptions));
        }
        finally{if((bool)Call("PrepareNormalExit")!)Call("CompleteNormalExit");}
    }
}

