using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using WorkIsland.Core;

namespace WorkIsland.App;

public static class WindowsActivity
{
    [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size; public uint Time; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RawDevice { public ushort Page,Usage; public uint Flags; public nint Target; }
    [StructLayout(LayoutKind.Sequential)] private struct RawHeader { public uint Type,Size;public nint Device,WParam; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput info);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd,uint flags);
    [DllImport("user32.dll")] private static extern bool RegisterRawInputDevices(RawDevice[] devices,uint count,uint size);
    [DllImport("user32.dll")] private static extern uint GetRawInputData(nint input,uint command,out RawHeader header,ref uint size,uint headerSize);
    [DllImport("user32.dll",EntryPoint="GetRawInputData")] private static extern uint GetRawInputPacket(nint input,uint command,[Out] byte[] data,ref uint size,uint headerSize);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    private static long? pointerAt;
    private static long keyboardAt;
    private static nint pointerWindow;
    private static HwndSource? inputSource;
    private static readonly byte[] inputPacket=new byte[64];
    private static Point? lastPointerPosition;
    private static Point? pointerLocation;
    private static bool pointerWindowNeedsRefresh;
    private static readonly BrowserSampler browser = new();

    internal static void Attach(nint handle)
    {
        if(inputSource is not null)return;
        // Only interaction kind + time + cursor position are retained. Press/release
        // and modifier category are transient; no key values, characters or packets are stored.
        var devices=new[]{new RawDevice{Page=1,Usage=2,Flags=0x100,Target=handle},new RawDevice{Page=1,Usage=6,Flags=0x100,Target=handle}};
        if(!RegisterRawInputDevices(devices,2,(uint)Marshal.SizeOf<RawDevice>()))return;
        lastPointerPosition=GetCursorPos(out var point)?point:null;
        inputSource=HwndSource.FromHwnd(handle);inputSource?.AddHook(Input);
    }
    internal static void Detach()
    {
        if(inputSource is null)return;
        inputSource.RemoveHook(Input);inputSource=null;
        RegisterRawInputDevices([new(){Page=1,Usage=2,Flags=1},new(){Page=1,Usage=6,Flags=1}],2,(uint)Marshal.SizeOf<RawDevice>());
        pointerAt=null;keyboardAt=0;pointerWindow=0;lastPointerPosition=null;pointerLocation=null;pointerWindowNeedsRefresh=false;Array.Clear(inputPacket);browser.Clear();
    }
    private static nint Input(nint hwnd,int message,nint wParam,nint lParam,ref bool handled)
    {
        if(message!=0xFF)return 0;
        var headerSize=(uint)Marshal.SizeOf<RawHeader>();var size=headerSize;
        if(GetRawInputData(lParam,0x10000005,out var header,ref size,headerSize)==uint.MaxValue || header.Type>1 || header.Size<headerSize || header.Size>inputPacket.Length)return 0;
        try
        {
            size=(uint)inputPacket.Length;
            var read=GetRawInputPacket(lParam,0x10000003,inputPacket,ref size,headerSize);
            if(read==uint.MaxValue || read!=header.Size)return 0;
            Point? position=null;var absoluteMoved=false;
            if(header.Type==0)
            {
                if(!GetCursorPos(out var point))return 0;
                absoluteMoved=lastPointerPosition is not Point previous || point.X!=previous.X || point.Y!=previous.Y;
                lastPointerPosition=point;
                position=point;
            }
            var activity=RecordRawActivity(header.Type,inputPacket,(int)headerSize,(int)(read-headerSize),absoluteMoved,0,Stopwatch.GetTimestamp());
            if(activity==RawActivity.Pointer){pointerLocation=position;pointerWindowNeedsRefresh=true;}
        }
        finally{Array.Clear(inputPacket);}
        return 0;
    }
    private static RawActivity RecordRawActivity(uint type,byte[] packet,int offset,int length,bool absoluteMoved,nint target,long timestamp)
    {
        var activity=RawInputActivity.Classify(type,packet,offset,length,absoluteMoved);
        if(activity==RawActivity.Pointer){pointerAt=timestamp;pointerWindow=target;}
        else if(activity==RawActivity.Keyboard)keyboardAt=timestamp;
        return activity;
    }
    internal static (string? Identifier,string? Name) Application(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd,out var pid);
        if(pid==0)return (null,null);
        try{using var process=Process.GetProcessById((int)pid);return (process.ProcessName+".exe",process.ProcessName);}
        catch(Exception error)when(error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception){return (null,null);}
    }
    internal static nint EffectiveWindow
    {
        get
        {
            if(pointerAt is not long time || time<keyboardAt)return GetForegroundWindow();
            // Like Mac, coalesce pointer events until evaluation. Resolve only
            // the last meaningful pointer location, and keep it cached until
            // another pointer event, rather than following stationary focus.
            if(pointerWindowNeedsRefresh)
            {
                pointerWindowNeedsRefresh=false;
                pointerWindow=pointerLocation is {} point?GetAncestor(WindowFromPoint(point),2):0;
            }
            return pointerWindow;
        }
    }
    public static ActivityInput Snapshot(bool readWebsites=false,double awayThreshold=30)
    {
        var last = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        var idle = GetLastInputInfo(ref last) ? unchecked((uint)Environment.TickCount - last.Time) / 1000.0 : double.PositiveInfinity;
        readWebsites=readWebsites&&double.IsFinite(idle)&&idle<awayThreshold;
        var foreground=GetForegroundWindow();var effective=EffectiveWindow;
        var front=Application(foreground);var pointer=Application(effective);
        var usesPointer=pointerAt is long time && time>=keyboardAt;
        var identifier=usesPointer?pointer.Identifier:front.Identifier;
        var url=readWebsites && identifier is not null && RecognitionRules.Browsers.Contains(identifier,StringComparer.OrdinalIgnoreCase)
            ? browser.Read(effective,identifier):null;
        if(!readWebsites || identifier is null || !RecognitionRules.Browsers.Contains(identifier,StringComparer.OrdinalIgnoreCase))browser.Clear();
        // Unresolved pointer is explicit unknown; do not fall back to stale focus.
        return new(front.Identifier,front.Name,idle,usesPointer?pointer.Identifier??"":null,usesPointer?pointer.Name:null,url);
    }
}
