using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunInstanceControlChecks(string output,Action<string,bool,object?> check)
    {
        var folder=Path.Combine(output,"instance-control");Directory.CreateDirectory(folder);
        var directory=Path.Combine(folder,"in-process");Directory.CreateDirectory(directory);
        var store=new AtomicStore(Path.Combine(directory,"state.json"));var state=new AppState{Settings=new(){Rules=[]}};
        var window=new IslandWindow(store,state,false,null,startupRegistration:new IsolatedStartupRegistration(Path.Combine(directory,"startup.json"),"isolated"),fullscreenSource:new FullscreenFixture());
        window.ShowActivated=false;window.Topmost=false;window.Loaded+=(_,_)=>((DispatcherTimer)typeof(IslandWindow).GetField("timer",Flags)!.GetValue(window)!).Stop();
        window.Show();await Task.Delay(80);var closed=false;window.Closed+=(_,_)=>closed=true;
        using var server=new InstanceControlServer(window,directory);
        try
        {
            var before=SHA256.HashData(File.ReadAllBytes(store.Path));
            var query=await InstanceControlClient.RequestAsync(directory,new("query"));
            check("instance query returns actual PID startup and data identity",query.Success&&query.Instance?.ProcessId==Environment.ProcessId&&query.Instance.DataDirectory==InstanceProtocol.Normalize(directory)&&query.Window?.Loaded==true,null);
            check("instance query identifies isolated startup provider without changing registration",query.Window?.StartupProvider=="isolated",query.Window?.StartupProvider);
            check("instance query leaves data and window visibility unchanged",before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(store.Path)))&&window.IsVisible,null);
            var wrongPid=await InstanceControlClient.RequestAsync(directory,new("exit",Environment.ProcessId+1,query.Instance!.StartedUtcTicks));
            var wrongStart=await InstanceControlClient.RequestAsync(directory,new("exit",Environment.ProcessId,"1"));
            check("exit rejects PID reuse and wrong startup identity",!wrongPid.Success&&!wrongStart.Success&&!closed,null);
            var unsupported=await InstanceControlClient.RequestAsync(directory,new("execute arbitrary command"));
            check("endpoint has no arbitrary command execution",!unsupported.Success&&unsupported.Status=="unsupported-command"&&!closed,null);
            using(var invalid=new NamedPipeClientStream(".",InstanceProtocol.PipeName(directory),PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly))
            {
                await invalid.ConnectAsync(1500);var prefix=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(prefix,1000000000);await invalid.WriteAsync(prefix);await invalid.FlushAsync();
            }
            var afterMalformed=await InstanceControlClient.RequestAsync(directory,new("query"));
            check("oversized packet is rejected and server recovers",afterMalformed.Success&&!closed,null);
            typeof(IslandWindow).GetField("applicationChooserOpen",Flags)!.SetValue(window,true);
            var modal=await InstanceControlClient.RequestAsync(directory,new("exit",Environment.ProcessId,query.Instance.StartedUtcTicks));
            check("normal exit refuses an active application chooser",!modal.Success&&modal.Status=="save-or-dialog-blocked"&&!closed,null);
            typeof(IslandWindow).GetField("applicationChooserOpen",Flags)!.SetValue(window,false);
            var held=directory+".held";var scope=Path.GetFullPath(folder)+Path.DirectorySeparatorChar;
            if(!Path.GetFullPath(directory).StartsWith(scope,StringComparison.OrdinalIgnoreCase)||!Path.GetFullPath(held).StartsWith(scope,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Fixture move outside scope.");
            Directory.Move(directory,held);File.WriteAllText(directory,"Owned fixture blocks saves; preserved after recovery.");
            var pending=new Session{ApplicationIdentifier="pending-exit.exe",ApplicationName="Pending fixture",StartedAt=DateTimeOffset.Now,EndedAt=DateTimeOffset.Now,DayKey=LogicalDay.Key(DateTimeOffset.Now,TimeZoneInfo.Local,0),EffectiveSeconds=60};state.Sessions.Add(pending);
            var timer=(DispatcherTimer)typeof(IslandWindow).GetField("timer",Flags)!.GetValue(window)!;timer.Start();
            var saveFailed=await InstanceControlClient.RequestAsync(directory,new("exit",Environment.ProcessId,query.Instance.StartedUtcTicks));
            check("endpoint reports failed save and keeps tracking plus memory record alive",!saveFailed.Success&&saveFailed.Status=="save-or-dialog-blocked"&&saveFailed.Error?.Contains("保存失败")==true&&!closed&&timer.IsEnabled&&state.Sessions.Contains(pending),null);
            timer.Stop();File.Move(directory,directory+".blocker-kept");Directory.Move(held,directory);
            var exit=await InstanceControlClient.RequestAsync(directory,new("exit",Environment.ProcessId,query.Instance.StartedUtcTicks));await Task.Delay(100);
            check("identity-checked endpoint uses normal saved exit",exit.Success&&exit.Status=="exit-prepared"&&closed&&store.Load().Sessions.Exists(s=>s.Id==pending.Id&&s.EffectiveSeconds==60),null);
        }
        finally
        {
            server.Dispose();await server.Completion.WaitAsync(TimeSpan.FromSeconds(3));
            if(!closed){typeof(IslandWindow).GetField("exiting",Flags)!.SetValue(window,true);window.Close();}
        }

        // Exercise the actual Program entry point, separate native GUI process,
        // actual CLI parsing/stdout, process disappearance and disk read-back.
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..",".."));
        // Use the product actually loaded by this validator, including isolated
        // build outputs. A guessed repository-relative path can select another
        // build or fail before the CLI checks have exercised any product code.
        var product=typeof(IslandWindow).Assembly.Location;
        var executable=Path.ChangeExtension(product,".exe");
        File.WriteAllText(Path.Combine(folder,"native-cli-binary.json"),JsonSerializer.Serialize(new{product,executable,appSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(product)))},AtomicStore.JsonOptions));
        var host=Path.GetFileNameWithoutExtension(Environment.ProcessPath)=="dotnet"?Environment.ProcessPath!:Path.GetFullPath(Path.Combine(root,"..","..","tools","dotnet-10.0.401","dotnet.exe"));
        async Task<(int Code,string Output)> Cli(params string[] arguments)
        {
            var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            info.Environment["DOTNET_ROOT"]=Path.GetDirectoryName(host);info.Environment["DOTNET_ROOT_X64"]=Path.GetDirectoryName(host);
            foreach(var argument in arguments)info.ArgumentList.Add(argument);
            using var client=Process.Start(info)!;var stdout=client.StandardOutput.ReadToEndAsync();var stderr=client.StandardError.ReadToEndAsync();
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(16));await client.WaitForExitAsync(timeout.Token);
            return(client.ExitCode,(await stdout)+(await stderr));
        }
        var unused=Path.Combine(folder,"must-not-create");
        foreach(var arguments in new[]{new[]{"--query-instance"},new[]{"--request-exit","--data-dir",unused},new[]{"--request-exit","--data-dir",unused,"--expect-pid","0","--expect-start-ticks","1"},new[]{"--query-instance","--preview","--data-dir",unused},new[]{"--query-instance","--data-dir",unused,"--expect-pid","1"}})
        {
            var result=await Cli(arguments);check("invalid control CLI rejects without opening GUI: "+string.Join(' ',arguments.Take(2)),result.Code==2&&!Directory.Exists(unused),null);
        }
        var missing=await Cli("--query-instance","--data-dir",unused);
        check("missing instance query creates no state directory",missing.Code==3&&!Directory.Exists(unused),null);
        var data=Path.Combine(folder,"native-data");Directory.CreateDirectory(data);var nativeStore=new AtomicStore(Path.Combine(data,"state.json"));
        var now=DateTimeOffset.Now;var record=new Session{ApplicationIdentifier="native-exit.exe",ApplicationName="Native exit fixture",StartedAt=now.AddHours(-1),EndedAt=now,DayKey=LogicalDay.Key(now,TimeZoneInfo.Local,0),EffectiveSeconds=3600};
        nativeStore.Save(new AppState{Settings=new(){Rules=[]},Sessions=[record]});
        // Native startup must recover a backup-only directory rather than silently
        // creating a fresh state. Preserve the original bytes as separate evidence.
        File.Move(nativeStore.Path,nativeStore.Path+".bak");
        File.Copy(nativeStore.Path+".bak",nativeStore.Path+".original-backup-kept");
        var launch=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true};launch.Environment["DOTNET_ROOT"]=Path.GetDirectoryName(host);launch.Environment["DOTNET_ROOT_X64"]=Path.GetDirectoryName(host);launch.ArgumentList.Add("--instance-review");launch.ArgumentList.Add("--data-dir");launch.ArgumentList.Add(data);
        using var process=Process.Start(launch)!;var expected=process.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            InstanceResponse? ready=null;var deadline=Stopwatch.StartNew();
            while(deadline.Elapsed.TotalSeconds<12&&!process.HasExited)
            {
                try{ready=await InstanceControlClient.RequestAsync(data,new("query"));if(ready.Window?.Loaded==true)break;}
                catch(Exception error)when(error is TimeoutException or IOException){ }
                await Task.Delay(100);
            }
            check("actual GUI process hosts lifecycle endpoint",ready?.Success==true&&ready.Instance?.ProcessId==process.Id&&ready.Instance.StartedUtcTicks==expected,null);
            if(ready?.Success!=true)throw new InvalidOperationException("Native lifecycle endpoint did not become ready.");
            var restoredAtStartup=nativeStore.Load();
            check("actual GUI startup restores backup-only history before exit",File.Exists(nativeStore.Path)&&restoredAtStartup.Sessions.Exists(s=>s.Id==record.Id&&s.EffectiveSeconds==3600),null);
            var queryCli=await Cli("--query-instance","--data-dir",data);var parsed=JsonSerializer.Deserialize<InstanceResponse>(queryCli.Output,AtomicStore.JsonOptions);
            check("actual query CLI returns machine-readable running state",queryCli.Code==0&&parsed?.Window?.Loaded==true&&parsed.Instance?.ProcessId==process.Id,null);
            check("actual Program startup uses live foreground without snapshot captures and exposes its current HWND",parsed?.Window?.Foreground is {Enabled:false,Active:false,Failed:false,Captures:0,WindowHandle:>0}&&parsed.Window.WpfRenderMode=="Default",parsed?.Window?.Foreground);
            using var self=Process.GetCurrentProcess();var wrong=await Cli("--request-exit","--data-dir",data,"--expect-pid",Environment.ProcessId.ToString(),"--expect-start-ticks",self.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            check("actual CLI cannot close a different process identity",wrong.Code==3&&!process.HasExited,null);
            var result=await Cli("--request-exit","--data-dir",data,"--expect-pid",process.Id.ToString(),"--expect-start-ticks",expected);
            check("actual exit CLI confirms OS process exit",result.Code==0&&process.HasExited&&process.ExitCode==0,new{result.Code});
            var retained=nativeStore.Load();check("native normal exit preserves exact saved session identity and value",retained.Sessions.Exists(s=>s.Id==record.Id&&s.EffectiveSeconds==3600),null);
            File.WriteAllText(Path.Combine(folder,"native-control.json"),JsonSerializer.Serialize(new{pid=process.Id,startedUtcTicks=expected,exitCode=process.ExitCode,startedFromBackupOnly=true,query=parsed,exitOutput=result.Output,recordRetained=retained.Sessions.Exists(s=>s.Id==record.Id)},AtomicStore.JsonOptions));
        }
        finally
        {
            if(!process.HasExited)
            {
                try{await InstanceControlClient.RequestAsync(data,new("exit",process.Id,expected));}catch(Exception error)when(error is IOException or TimeoutException){ }
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(35));await process.WaitForExitAsync(timeout.Token);
            }
        }
    }
}
