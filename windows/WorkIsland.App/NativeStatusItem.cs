using WorkIsland.Core;
using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Interop;

namespace WorkIsland.App;

// A native Shell status item: stable per data directory, keyboard activation,
// shell-provided anchor, and recovery after Explorer restarts. No Explorer patching.
internal sealed class NativeStatusItem : IDisposable
{
    private const uint Callback = 0x8000 + 73, Identifier = 0x5749;
    private readonly HwndSource source;
    internal delegate bool NotifyIconCall(uint message,ref IconData data);
    private readonly NotifyIconCall notifyIcon;
    private readonly Guid guid;
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private bool wanted, installed, disposed;
    private long? lastInstallAttempt;
    private nint icon;
    private string tooltip = "Work Island";
    public event Action? Selected;
    public event Action? ContextRequested;
    public event Action<Bitmap>? FrameSubmitted;
    public bool Installed => installed;
    public int FrameUpdates { get;private set; }
    public double IconSeconds { get;private set; }
    public double ShellSeconds { get;private set; }

    public NativeStatusItem(nint window, string identity,NotifyIconCall? notifyIcon=null)
    {
        this.notifyIcon=notifyIcon??Shell_NotifyIcon;
        source = HwndSource.FromHwnd(window)!;
        guid = new Guid(RuntimeCompat.HashData(Encoding.UTF8.GetBytes(identity.ToUpperInvariant())).Take(16).ToArray());
        source.AddHook(Message);
    }

    private IconData Data() => new()
    {
        Size = (uint)Marshal.SizeOf<IconData>(), Window = source.Handle, Id = Identifier,
        Flags = 1 | 2 | 4 | 0x20 | 0x80, Message = Callback, Icon = icon, Tip = tooltip, Guid = guid,
        Info = "", InfoTitle = ""
    };

    public bool SetVisible(bool value)
    {
        if(disposed&&value)return false;
        wanted = value;
        if (!value)
        {
            if (installed) { var remove = Data(); notifyIcon(2, ref remove); }
            installed = false; return true;
        }
        if (installed) return true;
        var data = Data();
        lastInstallAttempt=Stopwatch.GetTimestamp();
        installed = notifyIcon(0, ref data);
        if (installed) { data.Version = 4; notifyIcon(4, ref data); }
        return installed;
    }

    private void RetryMissingEntry()
    {
        // Use the existing once-per-second presentation refresh. Explorer may
        // reject an add while rebuilding its notification area; animation
        // frames and unchanged tooltip updates must not flood Shell retries.
        if(disposed||!wanted||installed)return;
        if(lastInstallAttempt is {} last&&RuntimeCompat.GetElapsedTime(last).TotalSeconds<1)return;
        SetVisible(true);
    }

    public void Update(Bitmap bitmap, string text)
    {
        if(disposed)return;
        FrameUpdates++;
        var started=Stopwatch.GetTimestamp();var next = bitmap.GetHicon();IconSeconds+=RuntimeCompat.GetElapsedTime(started).TotalSeconds;var previous = icon; icon = next;
        tooltip = text[..Math.Min(127, text.Length)];
        if (installed)
        {
            var data = Data();
            started=Stopwatch.GetTimestamp();
            if (!notifyIcon(1, ref data)) { installed = false; RetryMissingEntry(); }
            ShellSeconds+=RuntimeCompat.GetElapsedTime(started).TotalSeconds;
        }
        else RetryMissingEntry();
        if (previous != 0) DestroyIcon(previous);
        if(installed)FrameSubmitted?.Invoke(bitmap);
    }
    public void UpdateTooltip(string text)
    {
        if(disposed)return;
        var next=text[..Math.Min(127,text.Length)];if(next==tooltip){RetryMissingEntry();return;}tooltip=next;
        if(!installed){RetryMissingEntry();return;}
        var data=Data();data.Flags=4|0x20|0x80;
        if(!notifyIcon(1,ref data)){installed=false;RetryMissingEntry();}
    }

    public Rectangle? Bounds
    {
        get
        {
            var id = new IconIdentifier { Size = (uint)Marshal.SizeOf<IconIdentifier>(), Window = source.Handle, Id = Identifier, Guid = guid };
            return installed && Shell_NotifyIconGetRect(ref id, out var rect) == 0 && rect.Right > rect.Left && rect.Bottom > rect.Top
                ? Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom) : null;
        }
    }

    private nint Message(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)message == taskbarCreated && wanted) { installed = false; SetVisible(true); }
        if ((uint)message != Callback) return 0;
        var eventCode = (uint)(long)lParam & 0xffff;
        if (eventCode is 0x400 or 0x401) Selected?.Invoke(); // NIN_SELECT / NIN_KEYSELECT
        else if (eventCode == 0x7b) ContextRequested?.Invoke();
        handled = true; return 0;
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        SetVisible(false); source.RemoveHook(Message);
        if (icon != 0) DestroyIcon(icon); icon = 0;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct IconData
    {
        public uint Size; public nint Window; public uint Id, Flags, Message; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IconIdentifier { public uint Size; public nint Window; public uint Id; public Guid Guid; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint message, ref IconData data);
    [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref IconIdentifier id, out WindowsActivity.Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
}
