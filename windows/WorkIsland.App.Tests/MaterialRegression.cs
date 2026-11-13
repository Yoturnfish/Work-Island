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
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunMaterialChecks(string output,Action<string,bool,object?> check)
    {
        var folder=Path.Combine(output,"material");Directory.CreateDirectory(folder);
        var state=new AppState{Settings=new(){Rules=[],BackgroundStyle="glass",WindowX=300,WindowY=250}};
        var window=new IslandWindow(new AtomicStore(Path.Combine(folder,"state.json")),state,false,null,startupRegistration:new IsolatedStartupRegistration(Path.Combine(folder,"startup.json"),"isolated material"),fullscreenSource:new FullscreenFixture());
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        Color BackgroundColor()=>Field<Border>("OuterBorder").Background switch{SolidColorBrush solid=>solid.Color,LinearGradientBrush gradient=>gradient.GradientStops[0].Color,_=>Colors.Transparent};
        bool Backdrop()=>Field<object>("surfaces").GetType().GetProperty("BackdropVisible")!.GetValue(Field<object>("surfaces")) is true;
        bool Animated()=>typeof(IslandWindow).GetField("materialMotion",Flags)?.GetValue(window) is not null;
        double WashMask()=>(typeof(IslandWindow).GetField("StateWashMaterialMask",Flags)?.GetValue(window) as FrameworkElement)?.Opacity??1;
        int Distance(Color a,Color b)=>Math.Abs(a.A-b.A)+Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B);
        var samples=new List<object>();var watch=Stopwatch.StartNew();
        void Sample(string phase)=>samples.Add(new{phase,seconds=watch.Elapsed.TotalSeconds,color=BackgroundColor().ToString(),backdropVisible=Backdrop(),washMask=WashMask(),windowOpacity=window.Opacity,animated=Animated()});
        void Capture(string name)=>Call("SaveScreenRegion",Path.Combine(folder,name+".png"),23);
        void Complete(){for(var i=0;i<4;i++)Call("MoveOnboarding",1);Call("FinishOnboarding");}
        var background=new Window{Title="Work Island isolated material background",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,ShowActivated=false,Topmost=true,Left=210,Top=180,Width=470,Height=230,Background=new SolidColorBrush(Color.FromRgb(67,86,104))};
        window.ShowActivated=false;window.Topmost=true;window.Loaded+=(_,_)=>Field<DispatcherTimer>("timer").Stop();
        try
        {
            background.Show();window.Show();typeof(IslandWindow).GetField("fidelityBackground",Flags)!.SetValue(window,background);await Task.Delay(350);
            var native=Backdrop();var normal=BackgroundColor();Sample("normal glass");Capture("01-normal");
            Call("BeginOnboarding");var first=BackgroundColor();Sample("entry immediate");
            check("onboarding material begins from the presented foreground instead of snapping",Distance(normal,first)<3,new{normal=normal.ToString(),immediate=first.ToString()});
            check("entry retains the native backdrop while the foreground fades",!native||Backdrop(),null);
            await Task.Delay(90);Sample("entry midpoint");Capture("02-entry-midpoint");
            check("material has an actual intermediate presentation",Animated()&&Distance(BackgroundColor(),normal)>0&&(!native||Backdrop()),null);
            await Task.Delay(350);Sample("entry settled");Capture("03-onboarding");
            check("Mac onboarding uses the opaque fixed green-black background",BackgroundColor()==Color.FromRgb(6,15,10)&&WashMask()==0&&!Backdrop(),new{color=BackgroundColor().ToString(),washMask=WashMask()});
            var settled=BackgroundColor();Complete();Sample("finish immediate");
            check("finish material starts at the current onboarding foreground",Distance(BackgroundColor(),settled)<3,new{settled=settled.ToString(),immediate=BackgroundColor().ToString()});
            check("early completion removes the outgoing tutorial before the root fade",Field<Grid[]>("onboardingSteps").Count(step=>step.Visibility==Visibility.Visible)==1,null);
            await Task.Delay(90);Sample("finish midpoint");Capture("04-finish-midpoint");
            check("finish retains intermediate foreground and running material clock",Animated()&&Distance(BackgroundColor(),settled)>0&&Distance(BackgroundColor(),normal)>0,null);
            await Task.Delay(350);Sample("finish settled");Capture("05-finished");
            check("finish releases the clock and restores native glass without away wash",!Animated()&&BackgroundColor()==normal&&WashMask()==0&&(!native||Backdrop()),null);
            Call("BeginOnboarding");await Task.Delay(80);var retarget=BackgroundColor();Complete();Sample("rapid material reverse");
            check("rapid material reverse preserves the currently rendered color",Distance(BackgroundColor(),retarget)<3,new{before=retarget.ToString(),after=BackgroundColor().ToString()});
            await Task.Delay(50);state.Settings.ReduceMotion=true;Call("Render");check("reduced motion settles active material immediately",!Animated()&&BackgroundColor()==normal,null);
            state.Settings.ReduceMotion=false;Call("BeginOnboarding");await Task.Delay(50);window.Hide();check("hide settles material and hides native backdrop",!Animated()&&!Backdrop(),null);window.Show();await Task.Delay(80);
            state.Settings.ReduceMotion=true;state.Settings.BackgroundStyle="black";state.Settings.BackgroundOpacity=.6;Call("ApplyMaterial");Complete();Call("NotifyPointer",true);
            check("completed onboarding holds opacity until pointer leaves like Mac",window.Opacity==1,new{window.Opacity});
            var release=typeof(IslandWindow).GetMethod("ReleaseOnboardingOpacityOutside",Flags);release?.Invoke(window,[new Point(-10,-10)]);Call("ApplyWindowOpacity");
            check("leaving the window restores configured compact opacity",Math.Abs(window.Opacity-.6)<.001,new{window.Opacity});
            File.WriteAllText(Path.Combine(folder,"material-samples.json"),JsonSerializer.Serialize(new{nativeBackdropAvailable=native,samples,limitation="Actual WPF properties and owned native screen pixels over an isolated background. No Mac onboarding capture or real game."},AtomicStore.JsonOptions));
        }
        finally
        {
            typeof(IslandWindow).GetField("fidelityBackground",Flags)!.SetValue(window,null);
            typeof(IslandWindow).GetField("exiting",Flags)!.SetValue(window,true);window.Close();background.Close();
        }
    }
}
