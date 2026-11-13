using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunOpacityPixelChecks(string output,Action<string,bool,object?> check)
    {
        var folder=Path.Combine(output,"opacity-pixels");Directory.CreateDirectory(folder);
        var state=new AppState{Settings=new(){Rules=[],Paused=true,BackgroundOpacity=.6,WindowX=300,WindowY=250}};
        var window=new IslandWindow(new AtomicStore(Path.Combine(folder,"state.json")),state,false,null,startupRegistration:new IsolatedStartupRegistration(Path.Combine(folder,"startup.json"),"isolated opacity"),fullscreenSource:new FullscreenFixture());
        object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var background=new Window{Title="Work Island isolated opacity background",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,ShowActivated=false,Topmost=true,Left=210,Top=180,Width=470,Height=230,Background=Brushes.White};
        window.ShowActivated=false;window.Topmost=true;window.Loaded+=(_,_)=>((DispatcherTimer)typeof(IslandWindow).GetField("timer",Flags)!.GetValue(window)!).Stop();
        try
        {
            background.Show();window.Show();typeof(IslandWindow).GetField("fidelityBackground",Flags)!.SetValue(window,background);await Task.Delay(300);Call("NotifyPointer",false);await Task.Delay(120);
            var surfaces=typeof(IslandWindow).GetField("surfaces",Flags)!.GetValue(window)!;
            var shadow=(Window)surfaces.GetType().GetField("shadow",Flags)!.GetValue(surfaces)!;
            var visible=shadow.IsVisible;var withShadow=(System.Drawing.Color)Call("SampleMaterialColor")!;Call("SaveScreenRegion",Path.Combine(folder,"with-shadow.png"),23);
            shadow.Hide();await Task.Delay(180);var withoutShadow=(System.Drawing.Color)Call("SampleMaterialColor")!;Call("SaveScreenRegion",Path.Combine(folder,"without-shadow.png"),23);
            var difference=Math.Abs(withShadow.R-withoutShadow.R)+Math.Abs(withShadow.G-withoutShadow.G)+Math.Abs(withShadow.B-withoutShadow.B);
            check("black shadow is present for the low-opacity floating window",visible&&Math.Abs(window.Opacity-.6)<.001,null);
            check("external shadow does not darken the window interior a second time",difference<=6,new{withShadow,withoutShadow,difference,windowOpacity=window.Opacity});
            using var first=new System.Drawing.Bitmap(Path.Combine(folder,"with-shadow.png"));using var second=new System.Drawing.Bitmap(Path.Combine(folder,"without-shadow.png"));
            var padding=(int)Math.Ceiling(23*VisualTreeHelper.GetDpi(window).DpiScaleY);var outsideFirst=first.GetPixel(first.Width/2,first.Height-padding+5);var outsideSecond=second.GetPixel(second.Width/2,second.Height-padding+5);
            check("external shadow still darkens actual pixels below the window",outsideSecond.R-outsideFirst.R>3,new{outsideFirst,outsideSecond});
            File.WriteAllText(Path.Combine(folder,"opacity-pixels.json"),JsonSerializer.Serialize(new{windowOpacity=window.Opacity,withShadow,withoutShadow,interiorDifference=difference,outsideFirst,outsideSecond,limitation="Actual screen pixels over an isolated white background; shadow toggled only on the owned fixture, without changing the foreground."},AtomicStore.JsonOptions));
        }
        finally{typeof(IslandWindow).GetField("fidelityBackground",Flags)!.SetValue(window,null);typeof(IslandWindow).GetField("exiting",Flags)!.SetValue(window,true);window.Close();background.Close();}
    }
}
