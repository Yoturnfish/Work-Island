using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WorkIsland.App;

public partial class IslandWindow
{
    private bool nativeStateTransitionActive;
    private bool NativeStateFrameAllowed=>nativeForegroundEnabled&&!nativeForegroundFailed&&!exiting&&IsLoaded&&IsVisible
        &&!IsMenuBar&&(!isExpanded||NativeNavigationHome)&&!suspended&&!fullscreenSuppressed&&!startupSuppressed&&!PresentationReduced
        &&!OnboardingActive&&pageTransition is null&&materialMotion is null
        &&TitleEditor.Visibility!=Visibility.Visible&&!HasOpenPicker()&&nativeForegroundKeys.Count==0;

    private void BeginNativeStateTransition()
    {
        if(!nativeForegroundActive||!NativeStateFrameAllowed)return;
        nativeStateTransitionActive=true;nativeForegroundStamp=null;
        SetNativeStatusPulse(false);GoalProgress.SetNativePulseSuspended(false);
        DrawNativeStateTransition();
        if(nativeStateTransitionActive)statusAnimationTimer.Start();
    }
    private void StopNativeStateTransition()
    {
        if(!nativeStateTransitionActive)return;
        nativeStateTransitionActive=false;
        if(!IsMenuBar)statusAnimationTimer.Stop();
    }
    private void DrawNativeStateTransition()
    {
        if(!nativeStateTransitionActive||nativeForegroundBusy)return;
        if(!NativeStateFrameAllowed){StopNativeStateTransition();SuspendNativeForeground();return;}
        if(stateTransition is null&&compactInkMotion is null&&rootTransition is null&&!statusPulseHandoff)
        {StopNativeStateTransition();QueueNativeForeground();return;}
        var dpi=VisualTreeHelper.GetDpi(this);var size=new Size(OuterBorder.ActualWidth,OuterBorder.ActualHeight);
        if(size.Width<=0||size.Height<=0)return;
        nativeForegroundBusy=true;
        try
        {
            // Keep the currently presented native texture visible while the
            // next animated WPF frame is rasterized. Never swap whole renderers
            // at the start/end of a state crossfade.
            GoalProgress.SetNativeScanSuspended(true);OuterBorder.Opacity=1;UpdateLayout();
            var bitmap=new RenderTargetBitmap(Math.Max(1,(int)Math.Round(size.Width*dpi.DpiScaleX)),Math.Max(1,(int)Math.Round(size.Height*dpi.DpiScaleY)),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);
            bitmap.Render(OuterBorder);
            nativeForegroundSurface!.Upload(bitmap,size);nativeForegroundCaptures++;
            nativeGlyphSurface?.SetVisible(false);nativeCellsSurface?.SetVisible(false);
            nativeForegroundSurface.SetVisible(true);GoalProgress.SetNativeGroupOpacity(nativeLogicalOpacity);
        }
        catch(Exception error)when(ForegroundRecoverable(error))
        {nativeForegroundFailure=error.GetType().Name+": "+error.Message;nativeForegroundFailed=true;}
        finally{OuterBorder.Opacity=0;Opacity=1;nativeForegroundBusy=false;}
        if(nativeForegroundFailed){StopNativeStateTransition();SuspendNativeForeground();DisposeNativeForegroundSurfaces();}
    }
}
