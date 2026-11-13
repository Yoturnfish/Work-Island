using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using WorkIsland.App;
using WorkIsland.Core;
using Forms=System.Windows.Forms;

internal static class FullscreenReadOnly
{
    private const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Static;
    private static readonly Type Source=typeof(WindowsFullscreenSource);
    private static readonly List<object> Candidates=[];
    private static object? Call(string name,params object?[] args)=>Source.GetMethod(name,Flags)!.Invoke(null,args);
    internal static void Write(string path)
    {
        Candidates.Clear();var parameters=new object?[]{0};var result=Call("SHQueryUserNotificationState",parameters);
        var method=Source.GetMethod("EnumWindows",Flags)!;
        var callback=Delegate.CreateDelegate(method.GetParameters()[0].ParameterType,typeof(FullscreenReadOnly).GetMethod(nameof(Visit),Flags)!);
        method.Invoke(null,[callback,(nint)0]);
        File.WriteAllText(path,JsonSerializer.Serialize(new{hresult=result,notificationState=parameters[0],fullscreenActive=new WindowsFullscreenSource().IsFullscreenActive(),geometricCandidates=Candidates,scope="Read-only existing production P/Invokes and coverage rule. No window title/content capture, activation, input, hiding or process termination."},AtomicStore.JsonOptions));
    }
    private static bool Visit(nint window,nint ignored)
    {
        if(!(bool)Call("IsWindowVisible",window)!||(bool)Call("IsIconic",window)!)return true;
        var cloak=new object?[]{window,14,0,4};if((int)Call("DwmGetWindowAttribute",cloak)! !=0||(int)cloak[2]! !=0)return true;
        var className=new StringBuilder(256);Call("GetClassName",window,className,className.Capacity);
        if(className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")return true;
        var rectType=Source.GetMethod("GetClientRect",Flags)!.GetParameters()[1].ParameterType.GetElementType()!;
        var rect=new object?[]{window,Activator.CreateInstance(rectType)};if(!(bool)Call("GetClientRect",rect)!)return true;
        int RectValue(string name)=>(int)rectType.GetField(name)!.GetValue(rect[1])!;
        var pointType=Source.GetNestedType("NativePoint",BindingFlags.NonPublic)!;var point=new object?[]{window,Activator.CreateInstance(pointType)};
        if(!(bool)Call("ClientToScreen",point)!)return true;
        int PointValue(string name)=>(int)pointType.GetField(name)!.GetValue(point[1])!;
        var client=new Rectangle(PointValue("X"),PointValue("Y"),RectValue("Right")-RectValue("Left"),RectValue("Bottom")-RectValue("Top"));
        foreach(var screen in Forms.Screen.AllScreens)
        {
            if(!WindowsFullscreenSource.CoversMonitor(client,screen.Bounds))continue;
            var pidArgs=new object?[]{window,0u};typeof(WindowsActivity).GetMethod("GetWindowThreadProcessId",Flags)!.Invoke(null,pidArgs);
            var surfaces=typeof(IslandWindow).Assembly.GetType("WorkIsland.App.IslandSurfaces")!;
            var extendedStyle=(nint)surfaces.GetMethod("GetWindowLongPtr",Flags)!.Invoke(null,[window,-20])!;
            Candidates.Add(new{hwnd=window.ToInt64(),processId=pidArgs[1],windowClass=className.ToString(),extendedStyle=$"0x{extendedStyle.ToInt64():X}",client=new{client.X,client.Y,client.Width,client.Height},monitor=new{screen.Bounds.X,screen.Bounds.Y,screen.Bounds.Width,screen.Bounds.Height}});
        }
        return true;
    }
}
