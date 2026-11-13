using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    // No WPF application or real browser is opened. Synthetic zero / invalid
    // HWNDs make any default reader launched by Read stop at identity checking.
    // Only the owned pending task is replaced to control completion ordering;
    // all context invalidation and cache decisions execute in the production DLL.
    private static int RunBrowserSamplerChecks(string folder)
    {
        var output=Path.GetFullPath(folder);Directory.CreateDirectory(output);
        var type=typeof(IslandWindow).Assembly.GetType("WorkIsland.App.BrowserSampler")!;
        var read=type.GetMethod("Read")!;var clear=type.GetMethod("Clear")!;
        var rows=new List<object>();var failures=0;
        void Check(string name,bool passed,object? evidence=null){rows.Add(new{name,passed,evidence});if(!passed)failures++;}
        object New()=>Activator.CreateInstance(type,true)!;
        object? Get(object sampler,string field)=>type.GetField(field,Flags)!.GetValue(sampler);
        void Set(object sampler,string field,object? value)=>type.GetField(field,Flags)!.SetValue(sampler,value);
        string? Read(object sampler,nint window=0,string identifier="msedge.exe")=>(string?)read.Invoke(sampler,[window,identifier]);
        TaskCompletionSource<BrowserPage> Hold(object sampler)
        {
            var completion=new TaskCompletionSource<BrowserPage>(TaskCreationOptions.RunContinuationsAsynchronously);
            Set(sampler,"pending",completion.Task);return completion;
        }
        BrowserPage Page(string address="https://old.example/",nint window=0,string identifier="msedge.exe")=>new(identifier,"Synthetic browser",window,address,null);
        object Active(){var sampler=New();Read(sampler);return sampler;}
        try
        {
            var sampler=Active();var pending=Hold(sampler);
            Check("first asynchronous read returns no invented URL",Read(sampler) is null);
            pending.SetResult(Page());var current=Read(sampler);
            Check("unchanged context accepts completed current page",current=="https://old.example/",current);
            var rule=RecognitionRules.Website("old.example",true)!;
            Check("accepted current URL selects website ahead of browser app",ActivityDecision.Evaluate(new("msedge.exe","Edge",0,BrowserURL:current),[new("msedge.exe","Edge")],[rule],RecognitionRules.Browsers,30,false).Target?.Source=="website");

            sampler=Active();pending=Hold(sampler);
            Read(sampler,(nint)(-1));Read(sampler);
            pending.SetResult(Page());var stale=Read(sampler);
            Check("A to B to A rejects original A completion",stale is null,new{actual=stale});
            var decision=ActivityDecision.Evaluate(new("msedge.exe","Edge",0,BrowserURL:stale),[new("msedge.exe","Edge")],[rule],RecognitionRules.Browsers,30,false);
            Check("returning context does not attribute old page as current website",decision.Target?.Source=="application",new{decision.Target});
            var fresh=Hold(sampler);fresh.SetResult(Page("https://fresh.example/"));
            Check("fresh scan after context return can recover immediately",Read(sampler)=="https://fresh.example/");

            sampler=Active();pending=Hold(sampler);
            Read(sampler,0,"chrome.exe");Read(sampler);
            pending.SetResult(Page());stale=Read(sampler);
            Check("same HWND identifier round trip rejects old result",stale is null,new{actual=stale});

            sampler=Active();pending=Hold(sampler);
            for(var i=0;i<100;i++)Read(sampler,i%2==0?(nint)(-1):0);
            Check("context churn keeps one pending worker without queuing replacements",ReferenceEquals(Get(sampler,"pending"),pending.Task));
            pending.SetResult(Page());
            Check("context churn still discards the original worker result",Read(sampler) is null);

            sampler=Active();pending=Hold(sampler);clear.Invoke(sampler,null);pending.SetResult(Page());
            Check("clear discards late completion even on same HWND",Read(sampler) is null);

            sampler=Active();pending=Hold(sampler);
            Set(sampler,"requestedAt",Stopwatch.GetTimestamp()-(long)(Stopwatch.Frequency*2d));pending.SetResult(Page());
            Check("expired completion remains rejected",Read(sampler) is null);

            sampler=Active();pending=Hold(sampler);pending.SetException(new IOException("Synthetic browser read failure"));
            Check("failed read exposes no stale URL and permits recovery",Read(sampler) is null);
            fresh=Hold(sampler);fresh.SetResult(Page("https://recovered.example/"));
            Check("reader recovers after failed task",Read(sampler)=="https://recovered.example/");

            sampler=Active();pending=Hold(sampler);pending.SetResult(new("msedge.exe","Synthetic browser",0,null,"Address not readable"));
            Check("unavailable address cannot produce website match",Read(sampler) is null);

            sampler=Active();pending=Hold(sampler);pending.SetResult(Page(window:(nint)(-1)));
            Check("wrong result HWND remains rejected",Read(sampler) is null);

            sampler=Active();pending=Hold(sampler);pending.SetResult(Page(identifier:"chrome.exe"));
            Check("wrong browser identifier remains rejected",Read(sampler) is null);

            sampler=Active();pending=Hold(sampler);Read(sampler,0,"MSEDGE.EXE");pending.SetResult(Page());
            Check("identifier case alone does not invalidate current read",Read(sampler)=="https://old.example/");

            sampler=Active();pending=Hold(sampler);
            for(var i=0;i<100;i++)Read(sampler);
            pending.SetResult(Page());
            Check("repeated reads of unchanged context keep valid completion",Read(sampler)=="https://old.example/");

            sampler=Active();pending=Hold(sampler);pending.SetResult(Page());Read(sampler);
            pending=Hold(sampler);Read(sampler,(nint)(-1));Read(sampler);
            Check("leaving and returning clears cached URL immediately",Read(sampler) is null);
            pending.SetResult(Page());
            Check("old refresh cannot refill cache after context return",Read(sampler) is null);

            sampler=Active();pending=Hold(sampler);Read(sampler,(nint)(-1));pending.SetResult(Page());
            Check("completion for previous window is rejected while another is active",Read(sampler,(nint)(-1)) is null);
            fresh=Hold(sampler);fresh.SetResult(Page("https://second.example/",(nint)(-1)));
            Check("new window gets its own next scan after obsolete worker completes",Read(sampler,(nint)(-1))=="https://second.example/");
        }
        catch(Exception error){Check("sampler fixture completed",false,error.ToString());}
        File.WriteAllText(Path.Combine(output,"browser-sampler.json"),JsonSerializer.Serialize(new{productDllSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),checks=rows.Count,failures,rows,scope="Production BrowserSampler public Read/Clear with owned controlled pending tasks and synthetic zero/invalid HWNDs; default reader never reaches external UIA for these handles. No WPF application, actual browser URL, UI input, system setting or user data. Context round trip, expiry, serial work and recovery; TrackingDecision checks use resulting URL."},AtomicStore.JsonOptions));
        Console.WriteLine($"Browser sampler: {rows.Count-failures}/{rows.Count} passed; failures={failures}");return failures==0?0:1;
    }
}
