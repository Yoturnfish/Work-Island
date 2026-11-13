using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using WorkIsland.App;

// Diagnostic only: synchronous CBT notifications on this fixture's own UI
// thread and asynchronous WinEvents restricted to the same process/thread.
// Never changes a message, visibility or activation; always chains the CBT hook.
internal sealed class ModalNativeTracePicker(string folder):IApplicationPicker
{
    internal Action? OnNativeCreated{get;set;}
    public string? Select(Window owner)
    {
        using var trace=new ModalNativeTrace(owner,folder,OnNativeCreated);
        return new WindowsApplicationPicker(key=>(string)typeof(IslandWindow).GetMethod("Text",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)!.Invoke(owner,[key])!).Select(owner);
    }
}

internal sealed class ModalNativeTrace:IDisposable
{
    private readonly Window owner;
    private readonly nint ownerHandle;
    private readonly string path;
    private readonly Stopwatch watch=Stopwatch.StartNew();
    private readonly HashSet<nint> seen=[];
    private readonly HookCallback hookCallback;
    private readonly WinEventCallback eventCallback;
    private nint hook,eventHook;
    private readonly Action? onCreated;
    private bool creationObserved;
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    internal ModalNativeTrace(Window owner,string folder,Action? onCreated)
    {
        this.onCreated=onCreated;
        this.owner=owner;ownerHandle=new WindowInteropHelper(owner).Handle;path=Path.Combine(folder,"native-dialog-events.jsonl");
        hookCallback=OnHook;eventCallback=OnEvent;
        hook=SetWindowsHookEx(5,hookCallback,0,GetCurrentThreadId());
        eventHook=SetWinEventHook(0x8000,0x8003,0,eventCallback,(uint)Environment.ProcessId,GetCurrentThreadId(),0);
        Write(new{kind="installed",ownProcess=Environment.ProcessId,ownThread=GetCurrentThreadId(),cbt=hook!=0,winEvent=eventHook!=0});
        if(hook==0||eventHook==0){Dispose();throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Own-window trace registration failed");}
    }
    private bool Target(nint window)
    {
        if(seen.Contains(window))return true;
        if(window==0)return false;
        GetWindowThreadProcessId(window,out var pid);if(pid!=Environment.ProcessId)return false;
        var name=new StringBuilder(128);GetClassName(window,name,name.Capacity);
        if(name.ToString()!="#32770")return false;
        // CBT may precede the assignment of GW_OWNER. The later event records
        // retain actual ownership; only this fixture thread is hooked.
        var actualOwner=GetWindow(window,4);if(actualOwner!=0&&actualOwner!=ownerHandle)return false;
        seen.Add(window);return true;
    }
    private nint OnHook(int code,nint wParam,nint lParam)
    {
        try{if(code is 3 or 4 or 5&&Target(wParam))Record(code switch{3=>"CBT_CREATE",4=>"CBT_DESTROY",_=>"CBT_ACTIVATE"},wParam,null);}
        catch(Exception error){Write(new{kind="trace-error",error=error.ToString()});}
        return CallNextHookEx(hook,code,wParam,lParam);
    }
    private void OnEvent(nint hook,uint eventId,nint window,int objectId,int childId,uint thread,uint eventMs)
    {
        try
        {
            if(objectId!=0||childId!=0||!Target(window))return;
            Record(eventId switch{0x8000=>"EVENT_CREATE",0x8001=>"EVENT_DESTROY",0x8002=>"EVENT_SHOW",_=>"EVENT_HIDE"},window,eventMs);
            if(eventId==0x8000&&!creationObserved&&GetWindow(window,4)==ownerHandle)
            {creationObserved=true;onCreated?.Invoke();}
        }
        catch(Exception error){Write(new{kind="trace-error",error=error.ToString()});}
    }
    private void Record(string kind,nint window,uint? eventMs)
    {
        bool Flag(string name)=>(bool)typeof(IslandWindow).GetField(name,Flags)!.GetValue(owner)!;
        var enumerated=false;EnumThreadWindows(GetCurrentThreadId(),(candidate,_)=>{if(candidate==window)enumerated=true;return true;},0);
        Write(new{kind,atMs=watch.Elapsed.TotalMilliseconds,eventMs,observedTickMs=unchecked((uint)Environment.TickCount),window=(long)window,actualOwner=(long)GetWindow(window,4),expectedOwner=(long)ownerHandle,enumerated,visible=IsWindowVisible(window),ownerVisible=owner.IsVisible,fullscreenSuppressed=Flag("fullscreenSuppressed"),ownedPopupsSuppressed=Flag("ownedPopupsSuppressed")});
    }
    private void Write(object value)=>File.AppendAllText(path,JsonSerializer.Serialize(value)+Environment.NewLine);
    public void Dispose()
    {
        var eventRemoved=eventHook==0||UnhookWinEvent(eventHook);eventHook=0;
        var cbtRemoved=hook==0||UnhookWindowsHookEx(hook);hook=0;
        Write(new{kind="disposed",atMs=watch.Elapsed.TotalMilliseconds,eventRemoved,cbtRemoved});
        GC.KeepAlive(hookCallback);GC.KeepAlive(eventCallback);
    }
    private delegate nint HookCallback(int code,nint wParam,nint lParam);
    private delegate void WinEventCallback(nint hook,uint eventId,nint window,int objectId,int childId,uint thread,uint eventMs);
    private delegate bool EnumCallback(nint window,nint parameter);
    [DllImport("user32.dll")]private static extern bool EnumThreadWindows(uint thread,EnumCallback callback,nint parameter);
    [DllImport("user32.dll",SetLastError=true)]private static extern nint SetWindowsHookEx(int idHook,HookCallback callback,nint module,uint thread);
    [DllImport("user32.dll")]private static extern nint CallNextHookEx(nint hook,int code,nint wParam,nint lParam);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]private static extern nint SetWinEventHook(uint first,uint last,nint module,WinEventCallback callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool UnhookWinEvent(nint hook);
    [DllImport("kernel32.dll")]private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(nint window,out uint process);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetClassName(nint window,StringBuilder text,int count);
    [DllImport("user32.dll")]private static extern nint GetWindow(nint window,uint command);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool IsWindowVisible(nint window);
}
