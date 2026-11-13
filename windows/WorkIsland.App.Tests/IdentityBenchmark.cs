using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Interop;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    [DllImport("kernel32.dll",EntryPoint="OpenProcess")]private static extern nint IdentityOpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32.dll",EntryPoint="CloseHandle")]private static extern bool IdentityClose(nint handle);
    [DllImport("kernel32.dll",EntryPoint="QueryFullProcessImageNameW",CharSet=CharSet.Unicode)]private static extern bool IdentityImage(nint process,uint flags,StringBuilder path,ref uint size);
    [DllImport("user32.dll",EntryPoint="GetWindowThreadProcessId")]private static extern uint IdentityPid(nint window,out uint pid);
    private static (string?,string?) PrototypeIdentity(nint window)
    {
        IdentityPid(window,out var pid);if(pid==0)return(null,null);
        var process=IdentityOpenProcess(0x1000,false,pid);if(process==0)return(null,null);
        try{var text=new StringBuilder(512);uint length=512;if(!IdentityImage(process,0,text,ref length))return(null,null);var name=Path.GetFileNameWithoutExtension(text.ToString());return(name+".exe",name);}
        finally{IdentityClose(process);}
    }
    private static void RunIdentityBenchmark(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        var production=typeof(WindowsActivity).GetMethod("Application",BindingFlags.Static|BindingFlags.NonPublic)!.CreateDelegate<Func<nint,(string? Identifier,string? Name)>>();
        var rows=new List<object>();
        var foreground=typeof(WindowsActivity).GetMethod("GetForegroundWindow",BindingFlags.Static|BindingFlags.NonPublic)!.CreateDelegate<Func<nint>>()();
        foreach(var (role,hwnd) in new[]{("owned",new WindowInteropHelper(window).Handle),("foreground",foreground)})
        {
            IdentityPid(hwnd,out var targetPid);var old=production(hwnd);var candidate=PrototypeIdentity(hwnd);
            check("native query prototype returns identical identity for "+role+" actual window",old.Identifier==candidate.Item1&&old.Name==candidate.Item2&&old.Identifier is not null,null);
            for(var round=0;round<3;round++)foreach(var mode in new[]{"production","prototype"})
            {
                for(var warm=0;warm<10;warm++){if(mode=="production")production(hwnd);else PrototypeIdentity(hwnd);}
                var allocated=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();
                for(var iteration=0;iteration<300;iteration++){if(mode=="production")production(hwnd);else PrototypeIdentity(hwnd);}
                watch.Stop();rows.Add(new{role,separateProcess=targetPid!=Environment.ProcessId,round,mode,lookups=300,elapsedMilliseconds=watch.Elapsed.TotalMilliseconds,allocatedBytes=GC.GetAllocatedBytesForCurrentThread()-allocated});
            }
        }
        File.WriteAllText(Path.Combine(output,"identity-benchmark.json"),JsonSerializer.Serialize(new{productDllSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),rows,scope="Actual owned HWND and current foreground HWND identity queries only; no titles, path, names or content retained, no input, UI automation, activation or process changes. Same UI thread synchronous microbenchmark. Prototype is not production or complete compatibility implementation."},AtomicStore.JsonOptions));
    }
}
