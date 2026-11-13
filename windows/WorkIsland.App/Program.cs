using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using WorkIsland.Core;

namespace WorkIsland.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (!ValidateArguments(args)) return 2;
        var dailyDataDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WorkIsland");
        var dataDir = Value(args, "--data-dir") ?? dailyDataDir;
        string normalizedDataDir;
        try
        {
            if(dataDir.Contains('"') || string.IsNullOrWhiteSpace(dataDir))return 2;
            normalizedDataDir=Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDir));
        }
        catch(Exception error)when(error is ArgumentException or NotSupportedException or PathTooLongException)
        {Console.Error.WriteLine("数据目录格式无效。");return 2;}
        if(args.Contains("--query-instance")||args.Contains("--request-exit"))
        {
            var pid=int.TryParse(Value(args,"--expect-pid"),out var parsedPid)?parsedPid:0;
            var started=long.TryParse(Value(args,"--expect-start-ticks"),out var parsedTicks)?parsedTicks.ToString(System.Globalization.CultureInfo.InvariantCulture):null;
            return InstanceControlClient.RunAsync(normalizedDataDir,args.Contains("--request-exit"),pid,started).GetAwaiter().GetResult();
        }
        var smoke = args.Contains("--ui-smoke") || args.Contains("--native-smoke") || args.Contains("--restart-smoke") || args.Contains("--visual-review") || args.Contains("--fidelity-review") || args.Contains("--motion-review") || args.Contains("--stability-review");
        if ((smoke || args.Contains("--interaction-review") || args.Contains("--preview") || args.Contains("--instance-review")) && Value(args, "--data-dir") is null)
        { Console.Error.WriteLine("验收模式必须显式指定独立 --data-dir。"); return 2; }
        if((smoke || args.Contains("--interaction-review") || args.Contains("--preview") || args.Contains("--instance-review")) && string.Equals(
            normalizedDataDir,Path.TrimEndingDirectorySeparator(Path.GetFullPath(dailyDataDir)),StringComparison.OrdinalIgnoreCase))
        {Console.Error.WriteLine("验收模式不可使用日常数据目录。");return 2;}
        var stabilitySeconds=600;
        if(args.Contains("--stability-review") && Value(args,"--review-seconds") is { } duration
            && (!int.TryParse(duration,out stabilitySeconds) || stabilitySeconds<60 || stabilitySeconds>86400))return 2;
        var mutexName = "Local\\WorkIsland-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedDataDir.ToUpperInvariant())))[..20];
        using var mutex = new Mutex(true, mutexName, out var created);
        if (!created)
        {
            if(smoke || args.Contains("--interaction-review") || args.Contains("--preview")){Console.Error.WriteLine("验收数据目录已有运行实例。");return 2;}
            System.Windows.MessageBox.Show("Work Island 已在运行，请使用托盘图标打开。", "Work Island"); return 0;
        }
        try
        {
            // All processes launched from an installed directory keep its installer
            // mutex alive. The installer refuses upgrades/uninstall until they exit.
            using var installationMutex=InstallationIdentity.Acquire(AppContext.BaseDirectory);
            var store = new AtomicStore(Path.Combine(dataDir, "state.json"));
            var state = store.Load();
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            // A retained user preview must use the real login setting. Only
            // explicit review/smoke sessions receive a side-effect-free fixture.
            var isolatedReview=smoke || args.Contains("--interaction-review") || args.Contains("--instance-review");
            var executable=Path.Combine(AppContext.BaseDirectory,"WorkIsland.exe");
            IStartupRegistration startup=isolatedReview
                ? new IsolatedStartupRegistration(Path.Combine(dataDir,"startup-fixture.json"),StartupCommand.Create(executable,dataDir))
                : new WindowsStartupRegistration(executable,dataDir);
            var window = new IslandWindow(store, state, smoke, Value(args, "--smoke-output"), args.Contains("--native-smoke"), args.Contains("--restart-smoke"), args.Contains("--visual-review"),startup);
            window.FidelityReview = args.Contains("--fidelity-review");
            window.MotionReview = args.Contains("--motion-review");
            window.StabilityReview=args.Contains("--stability-review");window.StabilitySeconds=stabilitySeconds;
            window.BackgroundStabilityReview=window.StabilityReview && args.Contains("--start-minimized");
            if(args.Contains("--preview")){window.ShowInTaskbar=true;window.Title="Work Island · "+typeof(IslandWindow).Assembly.GetName().Version!.ToString(3)+" Preview";}
            if(args.Contains("--instance-review"))
            {
                window.ShowInTaskbar=true;window.Title="Work Island · Instance review";
                // A broken endpoint must not strand an owned test instance.
                // This uses the same save-before-exit path, never termination.
                var guard=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(30)};
                guard.Tick+=(_,_)=>{guard.Stop();if(window.PrepareNormalExit())window.CompleteNormalExit();};
                window.Loaded+=(_,_)=>guard.Start();window.Closed+=(_,_)=>guard.Stop();
            }
            if(window.StabilityReview){window.ShowActivated=false;window.Topmost=false;}
            // Only the isolated manual acceptance instance is exposed to window automation.
            if (args.Contains("--interaction-review"))
            {
                window.ShowInTaskbar = true; window.Title = "Work Island · Interaction review";
                window.Loaded += (_, _) => InteractionReview.Open(window, dataDir);
            }
            if(args.Contains("--start-minimized"))
            {
                window.ShowActivated=false;
                window.Loaded+=(_,_)=>window.Dispatcher.BeginInvoke(window.HideToTray);
            }
            app.MainWindow = window;
            InstanceControlServer? control=null;
            window.Loaded+=(_,_)=>{if(!smoke){window.EnableLiveForeground();control??=new InstanceControlServer(window,normalizedDataDir);}};
            window.Closed+=(_,_)=>control?.Dispose();
            app.DispatcherUnhandledException += (_, e) =>
            {
                e.Handled = true;
                var logged=TryLogError(dataDir,e.Exception);
                if (smoke) { app.Shutdown(1); return; }
                window.ShowError((logged?"发生错误，详见本地 errors.log：":"发生错误，日志暂时无法写入：") + e.Exception.Message);
            };
            window.PrepareFullscreenStartup();
            int exitCode;try{exitCode=app.Run(window);}finally{control?.Dispose();}
            return window.SmokeFailed ? 1 : exitCode;
        }
        catch (Exception error)
        {
            if (smoke) { Directory.CreateDirectory(dataDir); File.WriteAllText(Path.Combine(dataDir, "startup-error.txt"), error.ToString()); }
            else System.Windows.MessageBox.Show(error.Message, "Work Island 无法启动", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }
    private static bool TryLogError(string dataDir,Exception error)
    {
        try{File.AppendAllText(Path.Combine(dataDir,"errors.log"),error+Environment.NewLine);return true;}
        catch(Exception failure)when(failure is IOException or UnauthorizedAccessException){return false;}
    }
    private static bool ValidateArguments(string[] args)
    {
        var modes = new HashSet<string>(StringComparer.Ordinal)
        {
            "--ui-smoke", "--native-smoke", "--restart-smoke", "--visual-review",
            "--fidelity-review", "--motion-review", "--stability-review", "--interaction-review", "--preview", "--instance-review", "--query-instance", "--request-exit"
        };
        var values = new HashSet<string>(StringComparer.Ordinal)
        {
            "--data-dir", "--smoke-output", "--review-seconds", "--expect-pid", "--expect-start-ticks"
        };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var modeCount = 0;
        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            if (option != "--start-minimized" && !modes.Contains(option) && !values.Contains(option))
            { Console.Error.WriteLine("未知启动参数；每个参数及其值须独立传递。"); return false; }
            if (!seen.Add(option))
            { Console.Error.WriteLine("启动参数不可重复。"); return false; }
            if (modes.Contains(option)) modeCount++;
            if (!values.Contains(option)) continue;
            if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index])
                || args[index].StartsWith("--", StringComparison.Ordinal))
            { Console.Error.WriteLine("启动参数缺少有效值。"); return false; }
        }
        if (modeCount > 1)
        { Console.Error.WriteLine("每次启动只能选择一种验收模式。"); return false; }
        var control=seen.Contains("--query-instance")||seen.Contains("--request-exit");
        if(control&&(!seen.Contains("--data-dir")||seen.Contains("--start-minimized")||seen.Contains("--smoke-output")||seen.Contains("--review-seconds")))
        {Console.Error.WriteLine("实例命令必须显式指定数据目录，不可组合界面或验收参数。");return false;}
        if(seen.Contains("--request-exit"))
        {
            if(!int.TryParse(Value(args,"--expect-pid"),out var pid)||pid<=0||!long.TryParse(Value(args,"--expect-start-ticks"),out var ticks)||ticks<=0||ticks>DateTime.MaxValue.Ticks)
            {Console.Error.WriteLine("正常退出命令必须提供有效的 PID 和启动时间。");return false;}
        }
        else if(seen.Contains("--expect-pid")||seen.Contains("--expect-start-ticks"))
        {Console.Error.WriteLine("实例身份参数仅用于正常退出命令。");return false;}
        if (seen.Contains("--review-seconds") && !seen.Contains("--stability-review"))
        { Console.Error.WriteLine("--review-seconds 仅适用于稳定性验收。"); return false; }
        if (seen.Contains("--smoke-output") && modeCount == 0)
        { Console.Error.WriteLine("--smoke-output 必须与验收模式一同使用。"); return false; }
        return true;
    }
    private static string? Value(string[] args, string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
}
