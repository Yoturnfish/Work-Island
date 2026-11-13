using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WorkIsland.App;
using WorkIsland.Core;

namespace NativeAcceptance;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if(args.Length!=2)return 2;
        var data=Path.GetFullPath(args[0]);var output=Path.GetFullPath(args[1]);
        var root=Path.GetFullPath(Path.Combine(Environment.CurrentDirectory,"test-results"))+Path.DirectorySeparatorChar;
        if(!data.StartsWith(root,StringComparison.OrdinalIgnoreCase) || !output.StartsWith(root,StringComparison.OrdinalIgnoreCase) || Directory.Exists(output))return 2;
        Directory.CreateDirectory(data);
        var store=new AtomicStore(Path.Combine(data,"state.json"));
        var app=new System.Windows.Application { ShutdownMode=ShutdownMode.OnMainWindowClose };
        var startup=new IsolatedStartupRegistration(Path.Combine(data,"startup-fixture.json"),"isolated native acceptance; never registered with Windows");
        var island=new IslandWindow(store,store.Load(),false,output,true,false,false,startup)
        { ShowInTaskbar=true,Title="Work Island · Native acceptance" };
        app.MainWindow=island;
        var start=new Button { Content="开始原生验收",Name="StartNativeAcceptance",Width=160,Height=40,Margin=new Thickness(20) };
        var panel=new StackPanel();panel.Children.Add(new TextBlock {Text="Work Island · 原生输入验收",Margin=new Thickness(20),FontSize=18});panel.Children.Add(start);
        var control=new Window {Title="Work Island · Native acceptance control",Content=panel,Width=480,Height=190,Left=80,Top=400,ShowInTaskbar=true};
        var started=false;
        start.Click+=async (_,_)=>
        {
            if(started)return;started=true;start.IsEnabled=false;control.Hide();
            try
            {
                // The actual mouse click grants foreground to this process. Keep the
                // product's F24 guard unchanged and run its existing native scenario.
                typeof(IslandWindow).GetField("smoke",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(island,true);
                island.Activate();
                await (Task)typeof(IslandWindow).GetMethod("RunSmoke",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(island,null)!;
            }
            catch(Exception error){File.WriteAllText(Path.Combine(data,"harness-error.txt"),error.ToString());app.Shutdown(1);}
        };
        island.Loaded+=(_,_)=>control.Show();
        island.Closed+=(_,_)=>control.Close();
        var code=app.Run(island);
        return island.SmokeFailed ? 1 : code;
    }
}
