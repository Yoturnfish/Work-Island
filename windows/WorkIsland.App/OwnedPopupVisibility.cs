using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WorkIsland.App;

public partial class IslandWindow
{
    private bool ownedPopupsSuppressed;

    private void SuppressOwnedPopups()
    {
        // Transient color controls must close logically before Win32 hides
        // owned HWNDs, or restoring the island can restore the old form too.
        if(RecognitionColorButton.ContextMenu is {} colors)colors.IsOpen=false;
        var handle=new WindowInteropHelper(this).Handle;
        // Win32 owned windows are independent top-level windows: hiding their
        // owner does not hide the native file dialog. Restrict the operation
        // to this island's owner HWND; never enumerate or control other apps.
        if(handle!=IntPtr.Zero&&ShowOwnedPopups(handle,false))ownedPopupsSuppressed=true;
    }
    private void RestoreOwnedPopups()
    {
        if(!ownedPopupsSuppressed||fullscreenSuppressed||suspended||!IsVisible)return;
        var handle=new WindowInteropHelper(this).Handle;
        // This restores only popups hidden by the matching Win32 call, not
        // menus or dropdowns the application already closed before hiding.
        if(handle!=IntPtr.Zero&&ShowOwnedPopups(handle,true))ownedPopupsSuppressed=false;
    }
    [DllImport("user32.dll",SetLastError=true)]
    private static extern bool ShowOwnedPopups(nint owner,[MarshalAs(UnmanagedType.Bool)]bool show);
}
