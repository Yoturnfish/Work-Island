using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using Forms = System.Windows.Forms;

namespace WorkIsland.App;

public interface IFullscreenSource
{
    bool IsFullscreenActive();
}

// Read-only desktop inspection. No hooks into games, graphics APIs or input.
public sealed class WindowsFullscreenSource : IFullscreenSource
{
    public bool IsFullscreenActive()
    {
        int? notificationState=SHQueryUserNotificationState(out var state)==0?state:null;
        if(notificationState is 3 or 4)return true;
        var screens = Forms.Screen.AllScreens;
        var found = false;var passiveOverlay=false;
        EnumWindows((window, _) =>
        {
            WindowsActivity.GetWindowThreadProcessId(window, out var pid);
            if (pid == 0 || pid == Environment.ProcessId || !IsWindowVisible(window) || IsIconic(window)) return true;
            if (DwmGetWindowAttribute(window, 14, out var cloaked, 4) != 0 || cloaked != 0) return true;
            var name = new StringBuilder(256); GetClassName(window, name, name.Capacity);
            if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            if (!GetClientRect(window, out var rect)) return true;
            var origin = new NativePoint();
            if (!ClientToScreen(window, ref origin)) return true;
            var client = new Rectangle(origin.X, origin.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
            if(!screens.Any(screen => CoversMonitor(client, screen.Bounds)))return true;
            if(IsPassiveOverlay(unchecked((uint)GetWindowLong(window,-20)))){passiveOverlay=true;return true;}
            found=true;return false;
        }, 0);
        return ShouldSuppress(notificationState,found,passiveOverlay);
    }

    // A layered, click-through, nonactivating full-screen surface is a passive
    // overlay (for example a cursor/highlight), not an interactive application.
    // BUSY is ambiguous and can accompany these overlays. Preserve its conservative
    // fallback when no such full-screen overlay explains the geometric result.
    private static bool IsPassiveOverlay(long extendedStyle)=>(extendedStyle&0x08080020)==0x08080020;
    private static bool ShouldSuppress(int? notificationState,bool fullscreenWindow,bool passiveOverlay)=>
        notificationState is 3 or 4 || fullscreenWindow || notificationState==2&&!passiveOverlay;

    public static bool CoversMonitor(Rectangle client, Rectangle monitor) =>
        monitor.Width > 0 && monitor.Height > 0 && client.Width > 0 && client.Height > 0 &&
        client.Left <= monitor.Left + 1 && client.Top <= monitor.Top + 1 &&
        client.Right >= monitor.Right - 1 && client.Bottom >= monitor.Bottom - 1;

    private delegate bool EnumWindow(nint window, nint parameter);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] private static extern int GetWindowLong(nint window,int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out WindowsActivity.Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint window, ref NativePoint point);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
}
