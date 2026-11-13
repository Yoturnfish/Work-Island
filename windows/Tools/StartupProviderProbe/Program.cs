using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using WorkIsland.App;
using WorkIsland.Core;

internal static class Entry
{
    private static object? LoginValue(){using var key=Registry.CurrentUser.OpenSubKey(WindowsStartupRegistration.RunKey);return key?.GetValue(WindowsStartupRegistration.ValueName,null,RegistryValueOptions.DoNotExpandEnvironmentNames);}
    [STAThread] private static int Main(string[] args)
    {
        var index=Array.IndexOf(args,"--data-dir");if(index<0||index+1>=args.Length||!args.Contains("--start-minimized"))return 2;
        var data=Path.GetFullPath(args[index+1]);if(Directory.Exists(data))throw new IOException("Preserve evidence");Directory.CreateDirectory(data);
        var state=new AppState{Settings=new(){Paused=true,HasCompletedOnboarding=true,Rules=[]}};
        File.WriteAllText(Path.Combine(data,"state.json"),JsonSerializer.Serialize(state,AtomicStore.JsonOptions));
        var before=LoginValue();string? provider=null;bool? prepared=null;bool? visible=null;var captured=false;
        var watch=Stopwatch.StartNew();var flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        _=Task.Run(async()=>
        {
            while(watch.Elapsed.TotalSeconds<20&&!captured)
            {
                var app=Application.Current;
                if(app is not null&&!app.Dispatcher.HasShutdownStarted)
                    await app.Dispatcher.InvokeAsync(()=>
                    {
                        if(app.MainWindow is not IslandWindow window||!window.IsLoaded||captured)return;
                        provider=typeof(IslandWindow).GetField("startupRegistration",flags)!.GetValue(window)!.GetType().FullName;
                        visible=window.IsVisible;captured=true;
                        File.WriteAllText(Path.Combine(data,"provider-observed.json"),JsonSerializer.Serialize(new{provider,visible,pid=Environment.ProcessId,arguments=args},AtomicStore.JsonOptions));
                        prepared=(bool)typeof(IslandWindow).GetMethod("PrepareNormalExit",flags)!.Invoke(window,null)!;
                        if(prepared==true)typeof(IslandWindow).GetMethod("CompleteNormalExit",flags)!.Invoke(window,null);
                    },DispatcherPriority.ContextIdle);
                if(!captured)await Task.Delay(50);
            }
        });
        var code=WorkIsland.App.Program.Main(args);
        var expected=args.Contains("--instance-review")?typeof(IsolatedStartupRegistration).FullName:typeof(WindowsStartupRegistration).FullName;
        var unchanged=Equals(before,LoginValue());var passed=code==0&&captured&&prepared==true&&provider==expected&&unchanged;
        var assembly=typeof(IslandWindow).Assembly.Location;
        File.WriteAllText(Path.Combine(data,"result.json"),JsonSerializer.Serialize(new{passed,code,captured,prepared,provider,expected,visible,runValueUnchanged=unchanged,appSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly))),scope="Exact product Program.Main in a diagnostic host, explicit isolated data and minimized startup. Provider observed on its own dispatcher, product save-before-exit path. No login toggle or Run value write, no session switch, no physical input or ordinary EXE launch claim."},AtomicStore.JsonOptions));
        return passed?0:1;
    }
}
