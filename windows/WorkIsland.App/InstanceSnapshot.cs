namespace WorkIsland.App;

public partial class IslandWindow
{
    internal InstanceSnapshot CaptureInstanceSnapshot()
    {
        var bounds=statusItem?.Bounds;
        return new(IsLoaded,IsVisible,isExpanded,engine.Data.Settings.DisplayMode,menuPanelOpen,statusItem?.Installed==true,
            bounds is {} rect?new StatusAnchor(rect.X,rect.Y,rect.Width,rect.Height):null,engine.Data.Settings.Paused,suspended,fullscreenSuppressed,normalExitPrepared,
            new(nativeForegroundEnabled,nativeForegroundActive,nativeForegroundFailed,nativeForegroundFailure,nativeForegroundCaptures,Opacity,PresentedWindowOpacity,nativeForegroundKeys.Count,visualState,new System.Windows.Interop.WindowInteropHelper(this).Handle.ToInt64()),
            (System.Windows.PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource)?.CompositionTarget.RenderMode.ToString(),surfaces?.SystemEffectsSubscribed==true,
            startupRegistration is WindowsStartupRegistration?"windows":startupRegistration is IsolatedStartupRegistration?"isolated":"custom",tray.Visible,
            nativeStateTransitionActive||stateTransition is not null||IsMenuBar&&statusMotion.IsGlyphTransitioning(System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency));
    }
}
