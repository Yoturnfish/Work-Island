using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using WorkIsland.Core;

namespace WorkIsland.App;

public sealed record RunningApplication(string Identifier,string DisplayName,nint Window);
public sealed record BrowserPage(string BrowserIdentifier,string BrowserName,nint Window,string? Address,string? Unavailable);
public interface IRecognitionSource
{
    IReadOnlyList<RunningApplication> Applications();
    BrowserPage CurrentPage(RunningApplication application);
}
public sealed class WindowsRecognitionSource : IRecognitionSource
{
    public IReadOnlyList<RunningApplication> Applications()=>WindowsRecognition.Applications();
    public BrowserPage CurrentPage(RunningApplication app)=>WindowsRecognition.CurrentPage(app);
}

public static class WindowsRecognition
{
    private delegate bool EnumWindow(nint hwnd,nint param);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback,nint param);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(nint hwnd,StringBuilder text,int size);
    [DllImport("user32.dll")] private static extern int GetWindowLong(nint hwnd,int index);
    private static string Title(nint hwnd){var text=new StringBuilder(512);GetWindowText(hwnd,text,text.Capacity);return text.ToString();}

    public static IReadOnlyList<RunningApplication> Applications()
    {
        var apps=new List<RunningApplication>();
        EnumWindows((hwnd,_)=>
        {
            if(!IsWindowVisible(hwnd) || (GetWindowLong(hwnd,-20)&0x80)!=0 || Title(hwnd).Length==0)return true;
            WindowsActivity.GetWindowThreadProcessId(hwnd,out var pid);
            if(pid==RuntimeCompat.ProcessId)return true;
            var app=WindowsActivity.Application(hwnd);
            if(app.Identifier is null || app.Identifier.Equals("WorkIsland.exe",StringComparison.OrdinalIgnoreCase)
                || app.Identifier.Equals("ApplicationFrameHost.exe",StringComparison.OrdinalIgnoreCase))return true;
            var name=app.Name!;
            try{using var process=Process.GetProcessById((int)pid);var description=process.MainModule?.FileVersionInfo.FileDescription;if(!string.IsNullOrWhiteSpace(description))name=description!;}
            catch(Exception error)when(error is ArgumentException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception){ }
            apps.Add(new(app.Identifier,name,hwnd));return true;
        },0);
        return apps.GroupBy(a=>a.Identifier,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).OrderBy(a=>a.DisplayName,StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public static BrowserPage CurrentPage(RunningApplication app)
    {
        BrowserPage Missing(string reason)=>new(app.Identifier,app.DisplayName,app.Window,null,reason);
        if(!RecognitionRules.Browsers.Contains(app.Identifier,StringComparer.OrdinalIgnoreCase))return Missing("不支持此浏览器");
        var identity=WindowsActivity.Application(app.Window).Identifier;
        WindowsActivity.GetWindowThreadProcessId(app.Window,out var originalProcess);
        if(!string.Equals(identity,app.Identifier,StringComparison.OrdinalIgnoreCase))return Missing("窗口已关闭");
        try
        {
            if(PrivateIndicator(Title(app.Window)))return Missing("隐私窗口不读取");
            var root=AutomationElement.FromHandle(app.Window);
            var pending=new Queue<(AutomationElement Element,int Depth)>();pending.Enqueue((root,0));
            var watch=Stopwatch.StartNew();string? address=null;var count=0;
            while(pending.Count>0 && ++count<=450 && watch.ElapsedMilliseconds<350)
            {
                var (element,depth)=pending.Dequeue();var info=element.Current;
                if(info.ControlType==ControlType.Document || info.ControlType==ControlType.TabItem || info.IsPassword)continue;
                if(info.ControlType!=ControlType.Edit && PrivateIndicator(info.Name))return Missing("隐私窗口不读取");
                var id=info.AutomationId??"";var name=info.Name??"";
                var addressControl=info.ControlType==ControlType.Edit && (id.Contains("address",StringComparison.OrdinalIgnoreCase)
                    || id.Contains("urlbar",StringComparison.OrdinalIgnoreCase) || id.Contains("omnibox",StringComparison.OrdinalIgnoreCase)
                    || name.Contains("address",StringComparison.OrdinalIgnoreCase) || name.Contains("地址",StringComparison.OrdinalIgnoreCase)
                    || name.Contains("搜索或输入网址",StringComparison.OrdinalIgnoreCase));
                if(addressControl && !info.IsOffscreen)
                {
                    if(info.HasKeyboardFocus)return Missing("地址正在编辑，请完成导航后再识别");
                    if(element.TryGetCurrentPattern(ValuePattern.Pattern,out var pattern))address=((ValuePattern)pattern).Current.Value;
                }
                if(depth>=9)continue;
                for(var child=TreeWalker.ControlViewWalker.GetFirstChild(element);child is not null;child=TreeWalker.ControlViewWalker.GetNextSibling(child))
                {pending.Enqueue((child,depth+1));if(pending.Count>=450)break;}
            }
            WindowsActivity.GetWindowThreadProcessId(app.Window,out var currentProcess);
            if(currentProcess!=originalProcess || !string.Equals(WindowsActivity.Application(app.Window).Identifier,identity,StringComparison.OrdinalIgnoreCase))return Missing("窗口已变化");
            if(pending.Count>0)return Missing("浏览器暂未提供地址");
            return RecognitionRules.TryAddress(address,out var uri)
                ? new(app.Identifier,app.DisplayName,app.Window,uri!.AbsoluteUri,null):Missing("当前页地址不可读取");
        }
        catch(Exception error)when(error is ElementNotAvailableException or InvalidOperationException or COMException or UnauthorizedAccessException)
        {return Missing("浏览器暂未提供地址");}
    }
    private static bool PrivateIndicator(string? name) => name is not null && new[]{"InPrivate","Incognito","隐身","无痕","隐私浏览","Private Browsing"}.Any(s=>name.Contains(s,StringComparison.OrdinalIgnoreCase));
}

// One serialized UIA worker: no blocking timer and no growing work queue.
internal sealed class BrowserSampler
{
    private Task<BrowserPage>? pending;
    private nint requested;
    private string? requestedIdentifier;
    private BrowserPage? cached;
    private long completed;
    private long requestedAt;
    private int generation,pendingGeneration;
    public string? Read(nint window,string identifier)
    {
        // The active context can leave and return while one UIA read is pending.
        // Invalidate that worker on every context change, even if it eventually
        // returns the same HWND. Keep the worker serialized until it finishes.
        if(requested!=window || !string.Equals(requestedIdentifier,identifier,StringComparison.OrdinalIgnoreCase))
        {requested=window;requestedIdentifier=identifier;cached=null;completed=0;generation++;}
        if(pending?.IsCompleted==true)
        {
            if(pending.Status==TaskStatus.RanToCompletion && pending.Result.Window==window
                && pending.Result.BrowserIdentifier.Equals(identifier,StringComparison.OrdinalIgnoreCase) && generation==pendingGeneration
                && (Stopwatch.GetTimestamp()-requestedAt)/(double)Stopwatch.Frequency<=1.5){cached=pending.Result;completed=requestedAt;}
            else cached=null;
            pending=null;
        }
        if(pending is null)
        {
            requestedAt=Stopwatch.GetTimestamp();pendingGeneration=generation;
            pending=Task.Run(()=>WindowsRecognition.CurrentPage(new(identifier,identifier,window)));
        }
        return cached?.Window==window && cached.BrowserIdentifier.Equals(identifier,StringComparison.OrdinalIgnoreCase)
            && (Stopwatch.GetTimestamp()-completed)/(double)Stopwatch.Frequency<=1.5 ? cached.Address:null;
    }
    public void Clear(){cached=null;completed=0;requested=0;requestedIdentifier=null;generation++;}
}
