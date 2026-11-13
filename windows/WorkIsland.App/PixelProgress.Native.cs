using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace WorkIsland.App;

public sealed partial class PixelProgress
{
    private NativeProgressScan? nativeScan;
    private Window? nativeWindow;
    private FrameworkElement? nativeRoot;
    private HwndSource? nativePopupSource;
    private Func<NativeProgressScan?>? popupScanHost;
    private FrameworkElement? nativeClipHost;
    private bool nativeFailed,nativeBound,nativeSyncing,nativeSuspended;
    private string? nativeFailure;
    private double? nativeGroupOpacity;
    private readonly List<(DependencyObject Object,DependencyPropertyDescriptor Descriptor)> nativeObservers=[];
    private readonly List<Transform> nativeTransforms=[];
    private (Matrix Transform,double Alpha,Rect Clip,double RadiusX,double RadiusY,Size Window,DpiScale Dpi)? nativePresentation;

    // Opt-in after the HWND exists. Tests can exercise the full bridge before it
    // is enabled for ordinary previews; detached standalone controls keep WPF.
    internal void EnableNativeScan(Window window,FrameworkElement clipHost)
    {
        if(nativeRoot is not null)throw new InvalidOperationException("Native scan is already attached.");
        nativeWindow=window;nativeRoot=window;nativeClipHost=clipHost;BindNativePresentation();ConfigureMotion(true);
    }
    internal NativeProgressScan? NativeScanForPeer=>nativeScan;
    internal event EventHandler? NativeScanFailure;
    internal void UseWpfScan(Exception reason){FailNativeScan(reason);ConfigureMotion(true);}
    internal void EnableNativePopupScan(HwndSource source,FrameworkElement clipHost,Func<NativeProgressScan?>? sharedHost=null)
    {
        if(nativeFailed)return;
        if(ReferenceEquals(nativePopupSource,source))return;
        try
        {
            if(nativeRoot is not null)throw new InvalidOperationException("Native scan is already attached.");
            nativeRoot=source.RootVisual as FrameworkElement??throw new NotSupportedException("Popup has no framework visual root.");
            nativePopupSource=source;nativeClipHost=clipHost;popupScanHost=sharedHost;
            BindNativePresentation();ConfigureMotion(true);
        }
        catch(Exception error)when(NativeRecoverable(error)){FailNativeScan(error);ConfigureMotion(true);}
    }
    private void BindNativePresentation()
    {
        if(nativeRoot is null||nativeBound||!IsLoaded)return;
        nativeBound=true;LayoutUpdated+=NativeLayoutUpdated;
        if(nativeWindow is not null)nativeWindow.Closed+=NativeWindowClosed;
        if(nativePopupSource is not null)nativePopupSource.Disposed+=NativeWindowClosed;
        for(DependencyObject? current=this;current is not null;current=VisualTreeHelper.GetParent(current))
        {
            if(current is UIElement element)
                foreach(var property in new[]{OpacityProperty,RenderTransformProperty,VisibilityProperty,ClipProperty})
                {
                    var descriptor=DependencyPropertyDescriptor.FromProperty(property,element.GetType());
                    descriptor.AddValueChanged(element,NativePropertyChanged);nativeObservers.Add((element,descriptor));
                }
            if(ReferenceEquals(current,nativeRoot))break;
        }
        ObserveNativeTransforms();
    }
    private void ObserveNativeTransforms()
    {
        foreach(var transform in nativeTransforms)transform.Changed-=NativePropertyChanged;nativeTransforms.Clear();
        foreach(var item in nativeObservers.Where(item=>item.Descriptor.DependencyProperty==RenderTransformProperty))
        {
            var transform=((UIElement)item.Object).RenderTransform;
            if(transform.IsFrozen||nativeTransforms.Contains(transform))continue;
            transform.Changed+=NativePropertyChanged;nativeTransforms.Add(transform);
        }
    }
    private void NativePropertyChanged(object? sender,EventArgs args)
    {
        if(nativeSyncing)return;
        // RenderTransform may have been replaced during a navigation reversal.
        ObserveNativeTransforms();SyncNativePresentation();
    }
    private void NativeLayoutUpdated(object? sender,EventArgs args)=>SyncNativePresentation();
    private void NativeWindowClosed(object? sender,EventArgs args)=>ReleaseNativePresentation();
    private void ReleaseNativePresentation()
    {
        LayoutUpdated-=NativeLayoutUpdated;
        if(nativeWindow is not null)nativeWindow.Closed-=NativeWindowClosed;
        if(nativePopupSource is not null)nativePopupSource.Disposed-=NativeWindowClosed;
        foreach(var transform in nativeTransforms)transform.Changed-=NativePropertyChanged;nativeTransforms.Clear();
        foreach(var item in nativeObservers)item.Descriptor.RemoveValueChanged(item.Object,NativePropertyChanged);nativeObservers.Clear();
        nativeScan?.Dispose();nativeScan=null;nativePresentation=null;nativeBound=false;
    }
    internal void SetNativeScanSuspended(bool value)
    {
        if(nativeSuspended==value)return;
        nativeSuspended=value;
        try{ConfigureMotion(true);}
        catch(Exception error)when(NativeRecoverable(error)){FailNativeScan(error);ConfigureMotion(true);}
    }
    internal NativeForegroundSurface CreateNativeForegroundSurface()
    {
        if(nativeFailed||nativeWindow is null||!nativeBound)throw new InvalidOperationException(nativeFailure??"Native foreground host is unavailable.");
        if(nativeScan is null){nativeScan=new NativeProgressScan(new System.Windows.Interop.WindowInteropHelper(nativeWindow).Handle);nativeScan.SetScanning(false);SyncNativePresentation();}
        return nativeScan.CreateForegroundSurface();
    }
    internal NativeForegroundSurface CreateNativeForegroundSibling(NativeForegroundSurface source)=>nativeScan?.CreateSharedForegroundSurface(source)??throw new InvalidOperationException("Native foreground host is unavailable.");
    internal void SetNativeGroupOpacity(double? value)
    {
        nativeGroupOpacity=value;SyncNativePresentation();
    }
    private bool StartNativeScan()
    {
        if(nativeRoot is null||nativeFailed||nativeSuspended||!nativeBound||!IsLoaded)return false;
        try
        {
            nativeScan??=nativePopupSource is null
                ?new NativeProgressScan(new WindowInteropHelper(nativeWindow!).Handle)
                :popupScanHost is null?new NativeProgressScan(nativePopupSource.Handle)
                :(popupScanHost()??throw new InvalidOperationException("Shared popup compositor is not ready.")).CreateSibling();
            UpdateNativeGeometryCore();if(!nativeScan.Active)nativeScan.StartAtPhase(CurrentScanPhase);SyncNativePresentationCore();return true;
        }
        catch(Exception error)when(NativeRecoverable(error)){FailNativeScan(error);return false;}
    }
    private static bool NativeRecoverable(Exception error)=>error is COMException or NotSupportedException or InvalidOperationException or ArgumentException or EntryPointNotFoundException or DllNotFoundException;
    private void FailNativeScan(Exception error)
    {
        if(nativeFailed)return;
        nativeFailure=error.GetType().Name+": "+error.Message;nativeFailed=true;
        nativeScan?.Dispose();nativeScan=null;nativePresentation=null;
        NativeScanFailure?.Invoke(this,EventArgs.Empty);
    }
    private void UpdateNativeGeometry()
    {
        if(nativeScan is null)return;
        try{UpdateNativeGeometryCore();}
        catch(Exception error)when(NativeRecoverable(error)){FailNativeScan(error);mode="";}
    }
    private void UpdateNativeGeometryCore()
    {
        var geometry=new List<NativeProgressScan.Cell>();
        foreach(var (path,isFilled) in new[]{(filled,true),(unfilled,false)})
            if(path.Data is GeometryGroup group)
                foreach(var rectangle in group.Children.OfType<RectangleGeometry>())geometry.Add(new(rectangle.Rect,rectangle.RadiusX,rectangle.RadiusY,isFilled));
        nativeScan!.UpdateGeometry(geometry.OrderBy(cell=>cell.Bounds.X).ThenBy(cell=>cell.Bounds.Y).ToArray(),ActualWidth,filledBand.Width,accent.Color);
    }
    private void UpdateNativeColor()
    {
        if(nativeScan is null)return;
        try{nativeScan.UpdateColor(accent.Color);}
        catch(Exception error)when(NativeRecoverable(error)){FailNativeScan(error);mode="";ConfigureMotion(true);}
    }
    private void SyncNativePresentation()
    {
        if(nativeScan is null||nativeSyncing)return;
        try{nativeSyncing=true;SyncNativePresentationCore();}
        catch(Exception error)when(NativeRecoverable(error)){FailNativeScan(error);mode="";ConfigureMotion(true);}
        finally{nativeSyncing=false;}
    }
    private void SyncNativePresentationCore()
    {
        if(nativeScan is null||nativeRoot is null||nativeClipHost is null)return;
        // Foreground siblings also use this composition root. Hiding only the
        // compact progress control for navigation must not hide those siblings.
        if((!IsVisible&&!nativeGroupOpacity.HasValue)||unloaded||!nativeRoot.IsVisible){nativeScan.SetOpacity(0);nativePresentation=null;return;}
        var transform=TransformToAncestor(nativeRoot);
        var origin=transform.Transform(new(0,0));var x=transform.Transform(new(1,0));var y=transform.Transform(new(0,1));
        var matrix=new Matrix(x.X-origin.X,x.Y-origin.Y,y.X-origin.X,y.Y-origin.Y,origin.X,origin.Y);
        var opacity=1d;
        for(DependencyObject? current=this;current is not null;current=VisualTreeHelper.GetParent(current))
        {
            if(current is UIElement element)opacity*=element.Opacity;
            if(ReferenceEquals(current,nativeRoot))break;
        }
        var clip=nativePopupSource is not null?new RectangleGeometry(new Rect(0,0,nativeClipHost.ActualWidth,nativeClipHost.ActualHeight),10,10)
            :nativeClipHost.Clip as RectangleGeometry??throw new NotSupportedException("Native scan requires the rounded page clip.");
        var clipTransform=nativeClipHost.TransformToAncestor(nativeRoot);var clipOrigin=clipTransform.Transform(new(0,0));
        var clipX=clipTransform.Transform(new(1,0));var clipY=clipTransform.Transform(new(0,1));
        if(Math.Abs(clipX.Y-clipOrigin.Y)>.00001||Math.Abs(clipY.X-clipOrigin.X)>.00001)throw new NotSupportedException("Native scan clip cannot be rotated.");
        var sx=clipX.X-clipOrigin.X;var sy=clipY.Y-clipOrigin.Y;
        var clipRect=clipTransform.TransformBounds(clip.Rect);var radiusX=clip.RadiusX*sx;var radiusY=clip.RadiusY*sy;
        if(nativeGroupOpacity.HasValue)
        {
            // A foreground snapshot already contains the rounded WPF edge
            // coverage. Clipping at that edge again multiplies antialias alpha
            // and creates a bright fringe. Keep this guard outside captured
            // pixels; scan-only visuals retain the exact live page clip.
            clipRect.Inflate(2,2);radiusX+=2;radiusY+=2;
        }
        var size=new Size(nativeRoot.ActualWidth,nativeRoot.ActualHeight);var dpi=VisualTreeHelper.GetDpi(nativeRoot);
        opacity=nativeGroupOpacity??(nativeScan.Active?opacity:0);
        var presentation=(matrix,opacity,clipRect,radiusX,radiusY,size,dpi);
        if(nativePresentation is {} old && old.Equals(presentation))return;
        nativeScan.SetPresentation(matrix,opacity,new RectangleGeometry(clipRect,radiusX,radiusY),size,dpi);nativePresentation=presentation;
    }
}
