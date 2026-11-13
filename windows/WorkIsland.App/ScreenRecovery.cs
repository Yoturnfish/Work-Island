using System.Windows;
using System.Windows.Threading;

namespace WorkIsland.App;

public partial class IslandWindow
{
    private bool displayRecoveryPending;

    private void DisplaySettingsChanged(object? sender, EventArgs e) => QueueDisplayRecovery();

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        SuspendNativeForeground();
        base.OnDpiChanged(oldDpi,newDpi);
        if(IsLoaded)QueueDisplayRecovery();
    }

    private void QueueDisplayRecovery()
    {
        if(Dispatcher.HasShutdownStarted)return;
        // SystemEvents can arrive on its worker thread. Coalesce on the UI
        // dispatcher after WPF has applied the new DPI and suggested rectangle.
        Dispatcher.BeginInvoke(() =>
        {
            if(displayRecoveryPending || exiting || !IsLoaded)return;
            displayRecoveryPending=true;
            Dispatcher.BeginInvoke(() =>
            {
                displayRecoveryPending=false;
                if(exiting || !IsLoaded)return;
                if(IsMenuBar && menuPanelOpen)PositionMenuPanel();else ClampToScreen();
                surfaces?.UpdateMaterialGlass(MaterialGlassAmount);
                if(!IsMenuBar&&!OnboardingActive){engine.Data.Settings.WindowX=Left;engine.Data.Settings.WindowY=Top;}
                Persist();Render();
            },DispatcherPriority.ContextIdle);
        });
    }
}
