using WorkIsland.Core;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Controls;
using System.Windows.Threading;

namespace WorkIsland.App;

// The foreground remains a per-pixel layered HWND. A separate non-layered HWND lets
// the system host backdrop brush without flattening text or changing input routing.
internal sealed class IslandSurfaces : IDisposable
{
    private readonly IslandWindow island;
    private readonly Window shadow;
    private readonly Border shadowShape;
    private readonly Grid shadowContent;
    private Rect shadowHole;
    private Size shadowCanvas;
    private double shadowRadius;
    private readonly HwndSource foreground;
    private readonly Func<bool> effectsAllowed;
    private bool? lastEffectsAllowed;
    private DispatcherOperation? policyRefresh;
    private SystemEffectsMonitor? effectsMonitor;
    public bool SystemEffectsSubscribed=>effectsMonitor?.Subscribed==true;
    public int SystemEffectsWatchResult{get;private set;}=unchecked((int)0x80004001);
    private HwndSource? backdrop;
    private HostBackdrop? nativeBackdrop;
    private bool syncing, disposed, glass;
    private double glassAmount;
    private int regionWidth, regionHeight, regionRadius;
    public bool NativeBackdropAvailable { get; private set; }
    public int BackdropResult { get; private set; } = unchecked((int)0x80004001);
    public bool BackdropVisible => backdrop is not null && IsWindowVisible(backdrop.Handle);
    public bool ShadowVisible => shadow.IsVisible;
    public nint BackdropHandle => backdrop?.Handle ?? IntPtr.Zero;
    public nint ShadowHandle => new WindowInteropHelper(shadow).Handle;
    public void SetBackdropBlend(double value)
    {
        nativeBackdrop?.SetBlend(value);
    }

    private static bool ReadSystemEffectsAllowed()
    {
        using var transparency=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return !SystemParameters.HighContrast&&!Equals(transparency?.GetValue("EnableTransparency"),0);
    }
    public IslandSurfaces(IslandWindow island):this(island,ReadSystemEffectsAllowed,true){}
    internal IslandSurfaces(IslandWindow island,Func<bool> effectsAllowed):this(island,effectsAllowed,false){}
    private IslandSurfaces(IslandWindow island,Func<bool> effectsAllowed,bool monitorSystem)
    {
        this.island = island;
        this.effectsAllowed=effectsAllowed;
        foreground = (HwndSource)PresentationSource.FromVisual(island);
        shadowShape = new Border { Background = Brushes.Black, Margin = new Thickness(23),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 23, ShadowDepth = 4, Direction = 270, Opacity = .42 } };
        shadowContent=new Grid{IsHitTestVisible=false};shadowContent.Children.Add(shadowShape);
        shadow = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false,
            ShowActivated = false, IsHitTestVisible = false, Content = shadowContent, Title = "Work Island shadow" };
        var shadowSource = HwndSource.FromHwnd(new WindowInteropHelper(shadow).EnsureHandle())!;
        SetWindowLongPtr(shadowSource.Handle, -20, GetWindowLongPtr(shadowSource.Handle, -20) | 0x08000020 | 0x80);
        shadowSource.AddHook(PassThrough);
        if(monitorSystem&&RuntimeCompat.IsWindowsVersionAtLeast(10,0,22621))
        {
            try{effectsMonitor=new SystemEffectsMonitor(PostPolicyRefresh);SystemEffectsWatchResult=0;}
            catch(Exception error)when(error is COMException or InvalidOperationException or ArgumentException or EntryPointNotFoundException or DllNotFoundException)
            {SystemEffectsWatchResult=error.HResult;}
        }
        RefreshSystemBackdrop();
        foreground.AddHook(ForegroundChanged);
        island.IsVisibleChanged += VisibilityChanged;
        island.Closed += Closed;
    }
    private bool RefreshSystemBackdrop()
    {
        var allowed=false;
        try{allowed=RuntimeCompat.IsWindowsVersionAtLeast(10,0,22621)&&effectsAllowed()&&(effectsMonitor?.Enabled??true);}
        catch(Exception error)when(error is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException or COMException or InvalidOperationException)
        {BackdropResult=error.HResult;}
        if(lastEffectsAllowed==allowed&&(!allowed||NativeBackdropAvailable))return false;
        lastEffectsAllowed=allowed;var previous=NativeBackdropAvailable;
        if(!allowed){ReleaseBackdrop();return previous;}
        try
        {
            if(backdrop is null)
            {
                backdrop = new HwndSource(new HwndSourceParameters("Work Island acrylic")
                {
                    WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000020 | 0x80 | 0x00200000,
                    Width = 1, Height = 1, UsesPerPixelOpacity = false
                });
                backdrop.CompositionTarget.BackgroundColor=Colors.Transparent;backdrop.AddHook(PassThrough);
            }
            var enabled=1;Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(backdrop.Handle,17,ref enabled,4));
            nativeBackdrop=new HostBackdrop(backdrop.Handle);nativeBackdrop.SetBlend(.25*glassAmount);
            NativeBackdropAvailable=true;BackdropResult=0;
        }
        catch(Exception error)when(error is COMException or EntryPointNotFoundException or DllNotFoundException or InvalidOperationException)
        {BackdropResult=error.HResult;ReleaseBackdrop();}
        return previous!=NativeBackdropAvailable;
    }
    private void ReleaseBackdrop()
    {
        try{if(backdrop is not null)ShowWindow(backdrop.Handle,0);nativeBackdrop?.Dispose();}
        finally
        {
            nativeBackdrop=null;NativeBackdropAvailable=false;
            try{backdrop?.Dispose();}finally{backdrop=null;regionWidth=regionHeight=regionRadius=0;}
        }
    }
    private void QueuePolicyRefresh()
    {
        if(disposed||policyRefresh?.Status==DispatcherOperationStatus.Pending)return;
        // Read after WPF has invalidated its SystemParameters cache; bursts of
        // related notifications share one dispatcher operation, never a timer.
        policyRefresh=island.Dispatcher.BeginInvoke(()=>
        {
            policyRefresh=null;if(disposed)return;
            if(RefreshSystemBackdrop())island.RefreshSystemMaterial();
            Sync();
        },DispatcherPriority.Loaded);
    }
    private void PostPolicyRefresh()
    {
        // UISettings can notify from a background thread. Only the dispatcher
        // reads policyRefresh/disposed and touches the material HWNDs.
        if(island.Dispatcher.HasShutdownStarted)return;
        try{island.Dispatcher.BeginInvoke(QueuePolicyRefresh);}
        catch(InvalidOperationException)when(island.Dispatcher.HasShutdownStarted){}
    }

    public void Update(bool useGlass)
    {
        UpdateMaterialGlass(useGlass?1:0);
    }
    public void UpdateMaterialGlass(double amount)
    {
        amount=RuntimeCompat.Clamp(amount,0,1);
        var changed=Math.Abs(amount-glassAmount)>.000001;glassAmount=amount;
        if(changed)nativeBackdrop?.SetBlend(.25*amount);
        glass = amount>0;
        Sync();
    }
    private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) => Sync();
    private void Closed(object? sender, EventArgs args) => Dispose();
    private nint ForegroundChanged(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if(message is 0x001A or 0x031A)QueuePolicyRefresh(); // settings/theme
        if (message == 0x0047) Sync(); // window position
        // WPF has not yet applied WM_DPICHANGED while a source hook is running.
        // Its final position notification also syncs; queue one refresh after
        // layout so radius and shadow padding use the new PixelsPerDip.
        if (message == 0x02E0) island.Dispatcher.BeginInvoke(Sync,System.Windows.Threading.DispatcherPriority.Loaded);
        return 0;
    }
    private static nint PassThrough(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0084) { handled = true; return -1; } // HTTRANSPARENT
        if (message == 0x0021) { handled = true; return 3; } // MA_NOACTIVATE
        return 0;
    }
    private void Sync()
    {
        if (syncing || disposed) return;
        syncing = true;
        try
        {
            if (!island.IsVisible || island.SuppressMaterialSurfaces)
            {
                shadow.Hide(); if (backdrop is not null) ShowWindow(backdrop.Handle, 0);
                return;
            }
            if (!WindowsActivity.GetWindowRect(foreground.Handle, out var rect)) return;
            var dpi = VisualTreeHelper.GetDpi(island);
            var radius = (int)Math.Round(26 * island.ActualWidth / 344 * dpi.DpiScaleX * 2);
            var width = rect.Right - rect.Left; var height = rect.Bottom - rect.Top;
            if (glass && NativeBackdropAvailable && backdrop is not null)
            {
                if (regionWidth != width || regionHeight != height || regionRadius != radius)
                {
                    var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius, radius);
                    if (SetWindowRgn(backdrop.Handle, region, true) == 0) DeleteObject(region);
                    regionWidth = width; regionHeight = height; regionRadius = radius;
                }
                SetWindowPos(backdrop.Handle, foreground.Handle, rect.Left, rect.Top, width, height, 0x0010 | 0x0040);
                nativeBackdrop?.Resize(width, height);
            }
            else
            {
                if (backdrop is not null) ShowWindow(backdrop.Handle, 0);
            }
            if(glassAmount>=1){shadow.Hide();}
            else
            {
                var padding = (int)Math.Ceiling(23 * dpi.DpiScaleX);
                var paddingY = (int)Math.Ceiling(23 * dpi.DpiScaleY);
                shadow.Width = (width + padding*2) / dpi.DpiScaleX;
                shadow.Height = (height + paddingY*2) / dpi.DpiScaleY;
                var shadowCorner=26*island.ActualWidth/344;
                shadowShape.CornerRadius = new CornerRadius(shadowCorner);
                shadowShape.Margin=new Thickness(padding/dpi.DpiScaleX,paddingY/dpi.DpiScaleY,padding/dpi.DpiScaleX,paddingY/dpi.DpiScaleY);
                var hole=new Rect(padding/dpi.DpiScaleX,paddingY/dpi.DpiScaleY,width/dpi.DpiScaleX,height/dpi.DpiScaleY);
                var canvas=new Size(shadow.Width,shadow.Height);
                if(hole!=shadowHole||canvas!=shadowCanvas||shadowCorner!=shadowRadius)
                {
                    shadowHole=hole;shadowCanvas=canvas;shadowRadius=shadowCorner;
                    // DropShadowEffect also draws its filled source. Clip that
                    // interior after the effect, so this HWND adds only the
                    // external shadow rather than a second black window fill.
                    var clip=Geometry.Combine(new RectangleGeometry(new Rect(new Point(),canvas)),new RectangleGeometry(hole,shadowCorner,shadowCorner),GeometryCombineMode.Exclude,null);
                    clip.Freeze();shadowContent.Clip=clip;
                }
                shadow.Opacity = island.PresentedWindowOpacity*(1-glassAmount);
                shadow.Topmost = island.Topmost;
                if (!shadow.IsVisible) shadow.Show();
                SetWindowPos(ShadowHandle, foreground.Handle, rect.Left - padding, rect.Top - paddingY,
                    width + padding * 2, height + paddingY * 2, 0x0010 | 0x0040);
            }
        }
        finally { syncing = false; }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        policyRefresh?.Abort();policyRefresh=null;
        effectsMonitor?.Dispose();effectsMonitor=null;
        foreground.RemoveHook(ForegroundChanged);
        island.IsVisibleChanged -= VisibilityChanged;
        island.Closed -= Closed;
        try{ReleaseBackdrop();}finally{shadow.Close();}
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("gdi32.dll")] private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint window, nint region, bool redraw);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
}

