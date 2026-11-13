using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WorkIsland.App;

public partial class IslandWindow
{
    internal void EnableLiveForeground()
    {
        // Keep one live foreground for pages, input and state crossfades. There
        // is no full-window capture/visibility handoff during ordinary use.
        if(PresentationSource.FromVisual(this) is HwndSource source)
            source.CompositionTarget.RenderMode=RenderMode.Default;
        try{GoalProgress.EnableNativeScan(this,PageHost);}
        catch(Exception error)when(ForegroundRecoverable(error)){GoalProgress.UseWpfScan(error);}
    }
    private bool nativeForegroundEnabled,nativeForegroundActive,nativeForegroundBusy,nativeForegroundFailed;
    private bool nativePulseEnabled;
    private NativeForegroundSurface? nativeForegroundSurface;
    private NativeForegroundSurface? nativeGlyphSurface,nativeCellsSurface;
    private NativeForegroundInput? nativeForegroundInput;
    private AdornerLayer? nativeForegroundInputLayer;
    private DispatcherOperation? nativeForegroundRefresh;
    private ForegroundStamp? nativeForegroundStamp;
    private double nativeLogicalOpacity=1;
    private string? nativeForegroundFailure;
    private int nativeForegroundCaptures;
    private readonly HashSet<Key> nativeForegroundKeys=[];
    private HwndSource? nativeForegroundKeySource;
    [DllImport("user32.dll",EntryPoint="MapVirtualKeyW")]private static extern uint ForegroundMapVirtualKey(uint code,uint mapType);
    private readonly record struct ForegroundStamp(string Title,string Time,Color Accent,Color TitleInk,Color TimeInk,long Geometry,string Material,Size Size,DpiScale Dpi,double TitleSize,Matrix TimeTransform,string State,bool CellsPulse,bool Navigation,bool RootPressed);
    private bool NativeNavigationHome=>nativePulseEnabled&&isExpanded&&ReferenceEquals(activePage,NavigationHome);

    // Called by Program after normal startup layout. Isolated direct-window
    // tests can opt in separately; WPF remains the live transition/failure path.
    internal void EnableNativeForeground()
    {
        if(nativeForegroundEnabled)throw new InvalidOperationException("Native foreground is already enabled.");
        nativeLogicalOpacity=Opacity;nativeForegroundEnabled=true;
        try
        {
            GoalProgress.SetNativeScanSuspended(true);GoalProgress.EnableNativeScan(this,PageHost);
            nativeForegroundKeySource=PresentationSource.FromVisual(this) as HwndSource??throw new NotSupportedException("Native foreground needs a window message source.");
            nativeForegroundKeySource.AddHook(NativeForegroundKeyMessage);
        }
        catch(Exception error)when(ForegroundRecoverable(error))
        {
            nativeForegroundFailure=error.GetType().Name+": "+error.Message;nativeForegroundFailed=true;SuspendNativeForeground();return;
        }
        IsVisibleChanged+=NativeForegroundVisibilityChanged;SizeChanged+=NativeForegroundSizeChanged;
        PreviewMouseDown+=NativeForegroundMouseDown;PreviewMouseUp+=NativeForegroundMouseUp;
        PreviewKeyDown+=NativeForegroundKeyDown;PreviewKeyUp+=NativeForegroundKeyUp;
        LostMouseCapture+=NativeForegroundCaptureLost;
        Deactivated+=NativeForegroundDeactivated;
        QueueNativeForeground();
    }
    internal double PresentedWindowOpacity=>nativeForegroundActive?nativeLogicalOpacity:Opacity;
    // Separate explicit initialization keeps isolated tracking-only tests valid;
    // ordinary Program startup enables this after the foreground host is bound.
    internal void EnableNativePulses()
    {
        if(!nativeForegroundEnabled||nativePulseEnabled)throw new InvalidOperationException("Native pulses need one initialized foreground controller.");
        nativePulseEnabled=true;QueueNativeForeground();
    }
    private bool NativeForegroundReady=>nativeForegroundEnabled&&!nativeForegroundFailed&&!exiting&&IsLoaded&&IsVisible
        &&!suspended&&!fullscreenSuppressed&&!startupSuppressed&&!PresentationReduced&&!IsMenuBar&&(!isExpanded||NativeNavigationHome)
        &&!OnboardingActive&&onboardingRootMotion is null&&onboardingMotion is null&&materialMotion is null
        &&rootTransition is null&&pageTransition is null&&stateTransition is null&&compactInkMotion is null
        &&reminderClock is null
        &&visualState==engine.Current.State&&(visualState=="tracking"||nativePulseEnabled&&(visualState is "away" or "paused"))&&!statusPulseHandoff&&(NativeNavigationHome||CompactPanel.IsVisible&&CompactPanel.Opacity==1)
        &&TitleEditor.Visibility!=Visibility.Visible&&!HasOpenPicker()&&(!ExpandButton.IsPressed||nativePulseEnabled)&&nativeForegroundKeys.Count==0;
    private void QueueNativeForeground()
    {
        if(!nativeForegroundEnabled||nativeForegroundBusy||exiting||nativeForegroundFailed||nativeForegroundRefresh?.Status==DispatcherOperationStatus.Pending)return;
        nativeForegroundRefresh=Dispatcher.BeginInvoke(()=>{nativeForegroundRefresh=null;RefreshNativeForeground();},DispatcherPriority.Background);
    }
    private void RefreshNativeForeground()
    {
        if(nativeForegroundBusy)return;
        if(nativeStateTransitionActive){DrawNativeStateTransition();if(nativeStateTransitionActive)return;}
        if(!NativeForegroundReady){SuspendNativeForeground();return;}
        var dpi=VisualTreeHelper.GetDpi(this);var size=new Size(OuterBorder.ActualWidth,OuterBorder.ActualHeight);
        if(size.Width<=0||size.Height<=0)return;
        var pulse=!isExpanded&&visualState!="tracking";var cellsPulse=pulse&&GoalProgress.NativePulseWanted;
        var stamp=new ForegroundStamp(TitleLabel.Text,TimeLabel.Text,statusBrush.Color,compactTitleInk.Color,statusBrush.Color,GoalProgress.GeometryVersion,materialSignature,size,dpi,TitleLabel.FontSize,TimeLabel.RenderTransform.Value,visualState,cellsPulse,isExpanded,ExpandButton.IsPressed||CollapseButton.IsPressed);
        if(nativeForegroundActive&&nativeForegroundStamp==stamp)return;
        nativeForegroundBusy=true;
        try
        {
            // Keep the last submitted image visible until its replacement is
            // uploaded. Clearing the group here exposes the backdrop mid-frame.
            GoalProgress.SetNativeScanSuspended(isExpanded);
            nativeForegroundSurface??=GoalProgress.CreateNativeForegroundSurface();
            SetNativeStatusPulse(pulse);GoalProgress.SetNativePulseSuspended(cellsPulse);
            OuterBorder.Opacity=1;UpdateLayout();
            var glyphOpacity=StatusIcon.Opacity;var cellsOpacity=GoalProgress.PulseElement.Opacity;
            try
            {
                if(pulse)StatusIcon.Opacity=0;if(cellsPulse)GoalProgress.PulseElement.Opacity=0;
                var bitmap=new RenderTargetBitmap(Math.Max(1,(int)Math.Round(size.Width*dpi.DpiScaleX)),Math.Max(1,(int)Math.Round(size.Height*dpi.DpiScaleY)),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);
                bitmap.Render(OuterBorder);nativeForegroundSurface.Upload(bitmap,size);
            }
            finally{StatusIcon.Opacity=glyphOpacity;GoalProgress.PulseElement.Opacity=cellsOpacity;}
            UpdateNativePulsePlanes(size,dpi,pulse,cellsPulse);nativeForegroundCaptures++;
            nativeForegroundInputLayer??=AdornerLayer.GetAdornerLayer(OuterBorder)??throw new NotSupportedException("Native foreground needs an input adorner layer.");
            if(nativeForegroundInput is null){nativeForegroundInput=new(OuterBorder);nativeForegroundInputLayer.Add(nativeForegroundInput);}
            nativeForegroundActive=true;OuterBorder.Opacity=0;Opacity=1;
            nativeForegroundSurface.SetVisible(true);GoalProgress.SetNativeGroupOpacity(nativeLogicalOpacity);
            nativeForegroundStamp=stamp;surfaces?.UpdateMaterialGlass(MaterialGlassAmount);
        }
        catch(Exception error)when(ForegroundRecoverable(error))
        {
            nativeForegroundFailure=error.GetType().Name+": "+error.Message;nativeForegroundFailed=true;
        }
        finally{nativeForegroundBusy=false;}
        if(nativeForegroundFailed){SuspendNativeForeground();DisposeNativeForegroundSurfaces();}
    }
    private static bool ForegroundRecoverable(Exception error)=>error is COMException or InvalidOperationException or NotSupportedException or ArgumentException or EntryPointNotFoundException or DllNotFoundException;
    private void SuspendNativeForeground()
    {
        if(!nativeForegroundEnabled||nativeForegroundBusy)return;
        StopNativeStateTransition();
        nativeForegroundRefresh?.Abort();nativeForegroundRefresh=null;
        nativeForegroundBusy=true;
        try
        {
            try{nativeGlyphSurface?.SetVisible(false);}
            finally{try{nativeCellsSurface?.SetVisible(false);}finally{nativeForegroundSurface?.SetVisible(false);}}
        }
        catch(Exception error)when(ForegroundRecoverable(error)){nativeForegroundFailure=error.GetType().Name+": "+error.Message;nativeForegroundFailed=true;}
        finally
        {
            // Always restore live WPF even if the native device has failed.
            GoalProgress.SetNativeScanSuspended(true);GoalProgress.SetNativeGroupOpacity(null);
            GoalProgress.SetNativePulseSuspended(false);SetNativeStatusPulse(false);
            nativeForegroundActive=false;OuterBorder.Opacity=1;Opacity=nativeLogicalOpacity;
            if(nativeForegroundInput is not null){nativeForegroundInputLayer?.Remove(nativeForegroundInput);nativeForegroundInput=null;}
            nativeForegroundStamp=null;nativeForegroundBusy=false;surfaces?.UpdateMaterialGlass(MaterialGlassAmount);
        }
    }
    private void SetPresentedWindowOpacity(double value)
    {
        if(!nativeForegroundEnabled){Opacity=value;return;}
        nativeLogicalOpacity=value;
        if(nativeForegroundActive){Opacity=1;GoalProgress.SetNativeGroupOpacity(value);}else Opacity=value;
    }
    private void NativeForegroundVisibilityChanged(object sender,DependencyPropertyChangedEventArgs args){if(!IsVisible){nativeForegroundKeys.Clear();SuspendNativeForeground();}else QueueNativeForeground();}
    private void NativeForegroundSizeChanged(object sender,SizeChangedEventArgs args){SuspendNativeForeground();QueueNativeForeground();}
    private void NativeForegroundMouseDown(object sender,MouseButtonEventArgs args)
    {
        // A plain compact label/background click (including DragMove's nested
        // message loop) does not need another renderer. Actual controls still
        // use live WPF for their pressed/editing state.
        for(var source=args.OriginalSource as DependencyObject;source is not null&&source!=this;)
        {
            if(source is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.TextBoxBase)
            {
                if(nativePulseEnabled&&(ReferenceEquals(source,ExpandButton)||ReferenceEquals(source,CollapseButton))){QueueNativeForeground();return;}
                SuspendNativeForeground();return;
            }
            source=source is Visual or System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(source):LogicalTreeHelper.GetParent(source);
        }
    }
    private void NativeForegroundMouseUp(object sender,MouseButtonEventArgs args)=>QueueNativeForeground();
    private static Key NativeForegroundKey(KeyEventArgs args)=>args.Key==Key.System?args.SystemKey:args.Key==Key.ImeProcessed?args.ImeProcessedKey:args.Key;
    private void NativeForegroundKeyDown(object sender,KeyEventArgs args){nativeForegroundKeys.Add(NativeForegroundKey(args));SuspendNativeForeground();}
    private void NativeForegroundKeyUp(object sender,KeyEventArgs args){nativeForegroundKeys.Remove(NativeForegroundKey(args));QueueNativeForeground();}
    private nint NativeForegroundKeyMessage(nint hwnd,int message,nint wParam,nint lParam,ref bool handled)
    {
        // A control can clear WPF keyboard focus during KeyDown. Its native
        // HWND still receives KeyUp, even when no routed PreviewKeyUp follows.
        if(message is not (0x101 or 0x105))return 0;
        if((long)wParam is <=0 or >255)return 0;
        var virtualKey=(int)wParam;var extended=((long)lParam&0x1000000)!=0;
        var key=virtualKey switch
        {
            0x10=>KeyInterop.KeyFromVirtualKey((int)ForegroundMapVirtualKey((uint)(((long)lParam>>16)&0xff),3)),
            0x11=>extended?Key.RightCtrl:Key.LeftCtrl,
            0x12=>extended?Key.RightAlt:Key.LeftAlt,
            _=>KeyInterop.KeyFromVirtualKey(virtualKey)
        };
        if(nativeForegroundKeys.Remove(key))QueueNativeForeground();
        return 0; // Observe only; WPF and system shortcuts still process it.
    }
    private void NativeForegroundDeactivated(object? sender,EventArgs args){nativeForegroundKeys.Clear();QueueNativeForeground();}
    private void NativeForegroundCaptureLost(object sender,MouseEventArgs args)=>QueueNativeForeground();
    private void DisposeNativeForeground()
    {
        if(!nativeForegroundEnabled)return;
        SuspendNativeForeground();nativeForegroundEnabled=false;
        IsVisibleChanged-=NativeForegroundVisibilityChanged;SizeChanged-=NativeForegroundSizeChanged;
        PreviewMouseDown-=NativeForegroundMouseDown;PreviewMouseUp-=NativeForegroundMouseUp;
        PreviewKeyDown-=NativeForegroundKeyDown;PreviewKeyUp-=NativeForegroundKeyUp;LostMouseCapture-=NativeForegroundCaptureLost;
        Deactivated-=NativeForegroundDeactivated;nativeForegroundKeys.Clear();
        if(nativeForegroundKeySource is {IsDisposed:false})nativeForegroundKeySource.RemoveHook(NativeForegroundKeyMessage);
        nativeForegroundKeySource=null;
        DisposeNativeForegroundSurfaces();
    }
    private void DisposeNativeForegroundSurfaces()
    {
        try{nativeGlyphSurface?.Dispose();}
        finally{try{nativeCellsSurface?.Dispose();}finally{nativeForegroundSurface?.Dispose();nativeGlyphSurface=nativeCellsSurface=nativeForegroundSurface=null;nativeGlyphTextureStamp=null;nativeCellsTextureStamp=null;}}
    }
}

// Alpha-zero WPF pixels let Win32 input pass through even while Composition
// draws into the HWND. This non-hit-testable layer retains original controls.
internal sealed class NativeForegroundInput:Adorner
{
    private readonly Border border;
    private static readonly SolidColorBrush ink=CreateInk();
    private static SolidColorBrush CreateInk(){var brush=new SolidColorBrush(Color.FromArgb(1,255,255,255));brush.Freeze();return brush;}
    internal NativeForegroundInput(Border border):base(border){this.border=border;IsHitTestVisible=false;}
    protected override void OnRender(DrawingContext drawing)=>drawing.DrawRoundedRectangle(ink,null,new Rect(border.RenderSize),border.CornerRadius.TopLeft,border.CornerRadius.TopLeft);
}
