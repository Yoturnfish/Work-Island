using WorkIsland.Core;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace WorkIsland.App;

public partial class IslandWindow
{
    private readonly DispatcherTimer fullscreenTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool fullscreenSuppressed, restoreAfterFullscreen, startupSuppressed;
    private long? fullscreenClearSince;

    private void TrayMenuOpening(object? sender, System.ComponentModel.CancelEventArgs args)
    {
        // Both NotifyIcon and the native status item use this menu. Check at
        // its opening boundary, including requests between polling ticks.
        if(!smoke)RefreshFullscreenAvoidance();
        args.Cancel=exiting||suspended||fullscreenSuppressed||startupSuppressed;
    }

    internal void PrepareFullscreenStartup()
    {
        if (smoke) return;
        ShowActivated = false;
        startupSuppressed = fullscreenSource.IsFullscreenActive();
        if (startupSuppressed || IsMenuBar) { Opacity = 0; Topmost = false; }
    }

    private void RefreshFullscreenAvoidance()
    {
        if (exiting) return;
        var blocked = startupSuppressed || fullscreenSource.IsFullscreenActive();
        if (blocked)
        {
            fullscreenClearSince = null;
            if (fullscreenSuppressed) return;
            fullscreenSuppressed = true;
            restoreAfterFullscreen = IsVisible && !IsMenuBar;
            if(IsMenuBar)menuPanelOpen=false;
            CloseRecognitionPicker();
            if(onboardingGoal is not null)onboardingGoal.IsDropDownOpen=false;
            foreach (var picker in PreferencePickers)
                picker.IsDropDownOpen = false;
            if (RecognitionColorButton.ContextMenu is { } colors) colors.IsOpen = false;
            tray.ContextMenuStrip?.Close();
            focusReturn.Stop();
            SettleNavigation();
            SuppressOwnedPopups();
            Hide(); // IsVisibleChanged also hides the separate shadow/acrylic HWNDs.
            Topmost = false;
            Render(); // Settle breathing/reminder clocks without pausing tracking.
            return;
        }
        if (!fullscreenSuppressed || suspended) return;
        var now = Stopwatch.GetTimestamp();
        fullscreenClearSince ??= now;
        // Avoid flashing during resolution changes and short Alt+Tab transitions.
        if (RuntimeCompat.GetElapsedTime(fullscreenClearSince.Value, now).TotalSeconds < 1) return;
        fullscreenSuppressed = false; fullscreenClearSince = null;
        var restore = restoreAfterFullscreen; restoreAfterFullscreen = false;
        Topmost = engine.Data.Settings.Topmost;
        Render();
        if (!restore) return;
        var activated = ShowActivated;
        try { ShowActivated = false; Show(); ClampToScreen(); RestoreOwnedPopups(); }
        finally { ShowActivated = activated; }
    }

    internal void HideToTray() => HideIsland(null, new RoutedEventArgs());
}
