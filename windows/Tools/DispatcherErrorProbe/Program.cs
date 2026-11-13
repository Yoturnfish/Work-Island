using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static class Probe
{
    [STAThread] private static int Main(string[] args)
    {
        var output=Path.GetFullPath(args[0]);var mode=args[1];var live=args.Length>2&&args[2]=="live";
        if(Directory.Exists(output))throw new InvalidOperationException("Preserve prior evidence");
        Directory.CreateDirectory(output);var data=Path.Combine(output,"Data");Directory.CreateDirectory(data);
        var path=Path.Combine(data,"errors.log");var original="Retain original log.\n";
        if(mode=="directory")Directory.CreateDirectory(path);else File.WriteAllText(path,original);
        var store=new AtomicStore(Path.Combine(data,"state.json"));
        store.Save(new AppState{Settings=new Settings{Paused=true,Rules=[],HasCompletedOnboarding=true,ReduceMotion=true}});
        using var locked=mode=="locked"?new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read):null;
        var handled=false;var injected=false;var message="";var final=0;
        var watch=new DispatcherTimer(DispatcherPriority.Normal){Interval=TimeSpan.FromMilliseconds(20)};
        watch.Tick+=(_,_)=>
        {
            if(System.Windows.Application.Current?.MainWindow is not IslandWindow window||!window.IsLoaded)return;
            watch.Stop();window.Title="Work Island · 错误日志验收 · "+Environment.ProcessId;
            System.Windows.Application.Current.DispatcherUnhandledException+=(_,e)=>
            {
                handled=e.Handled;
                message=((TextBlock)window.FindName("MessageLabel")).Text;
                File.WriteAllText(Path.Combine(output,"observed.json"),JsonSerializer.Serialize(new{handled,message,pid=Environment.ProcessId,scope="Actual product Program.Main dispatcher exception handler; original fault deliberately injected in isolated data."},AtomicStore.JsonOptions));
                if(live)
                {
                    var finish=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(200)};
                    finish.Tick+=(_,_)=>
                    {
                        if(!File.Exists(Path.Combine(output,"finish.txt")))return;
                        finish.Stop();
                        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                        if((bool)typeof(IslandWindow).GetMethod("PrepareNormalExit",flags)!.Invoke(window,null)!)
                            typeof(IslandWindow).GetMethod("CompleteNormalExit",flags)!.Invoke(window,null);
                    };
                    finish.Start();
                }
            };
            window.Dispatcher.BeginInvoke(()=>{injected=true;throw new InvalidOperationException("dispatcher-log-review-sentinel");});
        };
        watch.Start();
        var productExit=WorkIsland.App.Program.Main(live
            ? new[]{"--instance-review","--data-dir",data}
            : new[]{"--ui-smoke","--data-dir",data,"--smoke-output",output});
        watch.Stop();locked?.Dispose();
        var logPreserved=mode=="directory"?Directory.Exists(path):File.ReadAllText(path).StartsWith(original,StringComparison.Ordinal);
        var logRecorded=mode=="writable"&&File.ReadAllText(path).Contains("dispatcher-log-review-sentinel",StringComparison.Ordinal);
        var startupError=File.Exists(Path.Combine(data,"startup-error.txt"));
        var checks=new[]{
            new{name="actual dispatcher fault injected",passed=injected},
            new{name="original exception handled despite log availability",passed=handled},
            new{name="logging failure does not become startup failure",passed=!startupError},
            new{name="preexisting log content or directory preserved",passed=logPreserved},
            new{name="writable log retains original exception",passed=mode!="writable"||logRecorded},
            new{name="product follows deliberate smoke failure or normal exit",passed=productExit==(live?0:1)},
            new{name="live error message describes actual log outcome",passed=!live||(message.Contains("dispatcher-log-review-sentinel",StringComparison.Ordinal)&&message.Contains(mode=="writable"?"errors.log":"日志暂时无法写入",StringComparison.Ordinal))}
        };
        foreach(var check in checks)if(!check.passed)final++;
        File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{mode,live,productExit,checks,failed=final,appSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location)))},AtomicStore.JsonOptions));
        Console.WriteLine($"{checks.Length} checks, {final} failed; product returned {productExit}");
        return final==0?0:1;
    }
}
