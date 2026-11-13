using System.Drawing;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace WorkIsland.App;

public partial class IslandWindow
{
    private NativeStatusItem? statusItem;
    private bool menuPanelOpen;
    private string statusTimeKey="";
    private readonly StatusItemMotion statusMotion=new();
    private readonly StatusGlyphArtwork statusArtwork=new();
    private int statusIconSize=32;
    private readonly DispatcherTimer statusAnimationTimer=new(DispatcherPriority.Render){Interval=TimeSpan.FromSeconds(1/StatusItemMotion.FrameRate)};
    private long statusDrawCalls;
    private double statusRasterSeconds;
    private double statusSnapshotAt,statusDisplaySeconds,statusWorkSeconds,statusProgress;
    private string statusDurationStyle="hoursMinutes",statusGoal="4";
    private bool statusAwayClock;
    private bool IsMenuBar => engine.Data.Settings.DisplayMode=="menuBar"&&!OnboardingActive;

    private void InitializeStatusItem()
    {
        if(statusItem is not null)return;
        statusItem=new NativeStatusItem(new WindowInteropHelper(this).Handle,store.Path);
        statusItem.Selected+=ToggleMenuPanel;
        statusItem.ContextRequested+=()=>tray.ContextMenuStrip?.Show(Forms.Cursor.Position);
        statusAnimationTimer.Tick+=(_,_)=>{if(nativeStateTransitionActive)DrawNativeStateTransition();else DrawMenuFrame();};
        IsVisibleChanged+=(_,_)=>{if(IsLoaded&&!exiting){UpdateTrayPresence();Render();}};
        PreviewKeyDown+=MenuKey;
    }
    private void MenuKey(object sender,KeyEventArgs args)
    {
        if(args.Key!=Key.Escape || !menuPanelOpen || args.Handled || HasOpenPicker())return;
        HideMenuPanel();args.Handled=true;
    }
    private (double Seconds,string Style) DisplayedTime(DateTimeOffset now) => engine.Data.Settings.AwayTimerEnabled && engine.Current.State=="away"
        ? (awaySince is {} start?Math.Max(0,(now-start).TotalSeconds):0,"minutesSeconds")
        : (engine.TodaySeconds(now),engine.Data.Settings.DurationStyle);

    private void UpdateMenuTime()
    {
        if(statusItem is null || exiting){statusAnimationTimer.Stop();return;}
        if(!IsMenuBar){if(!nativeStateTransitionActive)statusAnimationTimer.Stop();return;}
        var now=DateTimeOffset.Now;var monotonic=Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
        var snapshot=DisplayedTime(now);statusDisplaySeconds=snapshot.Seconds;statusDurationStyle=snapshot.Style;
        statusIconSize=(int)Math.Clamp(Math.Round(SystemParameters.SmallIconWidth*System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX),16,64);
        statusWorkSeconds=engine.TodaySeconds(now);statusProgress=engine.Progress(now);statusSnapshotAt=monotonic;
        statusAwayClock=engine.Data.Settings.AwayTimerEnabled&&engine.Current.State=="away";
        statusGoal=(engine.Data.Settings.GoalSeconds/3600).ToString("0.#");
        var reduced=suspended||fullscreenSuppressed||engine.Data.Settings.ReduceMotion||!SystemParameters.ClientAreaAnimation;
        var color=engine.Current.State switch{"tracking"=>ThemeColor,"paused"=>System.Windows.Media.Color.FromRgb(173,173,173),_=>System.Windows.Media.Color.FromRgb(255,149,0)};
        statusMotion.Retarget(Color.FromArgb(color.A,color.R,color.G,color.B),engine.Current.State,statusProgress,reduced,monotonic,
            awaySince is {} start?(now-start).TotalSeconds-10:null);
        DrawMenuFrame();
    }
    private void DrawMenuFrame()
    {
        statusDrawCalls++;
        if(statusItem is null || !IsMenuBar || exiting){statusAnimationTimer.Stop();return;}
        var monotonic=Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
        var seconds=statusDisplaySeconds+(statusAwayClock?Math.Max(0,monotonic-statusSnapshotAt):0);var style=statusDurationStyle;var color=statusMotion.ColorAt(monotonic);
        var blend=statusMotion.GlyphAt(monotonic);
        var phase=statusMotion.Mode=="scan"||statusMotion.IsGlyphTransitioning(monotonic)?statusMotion.ScanPhase(monotonic):0;
        var coffeePulse=statusMotion.CoffeePulse(monotonic);
        var display=DurationText.Compact(seconds,style);
        var tooltip=$"Work Island · {display} · {FormatDuration(statusWorkSeconds)} / {statusGoal}h";
        var key=$"{color}/{engine.Data.Settings.BackgroundStyle}/{statusIconSize}/{StatusGlyphArtwork.ProgressKey(statusProgress)}/{(int)Math.Round(blend.Clock*255)}/{(int)Math.Round(blend.Coffee*255)}/{(int)Math.Round(blend.Pause*255)}/{(int)(phase*128)}/{(int)Math.Round(coffeePulse*32)}";
        if(statusTimeKey!=key)
        {
            statusTimeKey=key;
            var rasterStarted=Stopwatch.GetTimestamp();
            using var bitmap=statusArtwork.Render(engine.Current.State,engine.Data.Settings.Accent,statusProgress,statusIconSize,color,engine.Data.Settings.BackgroundStyle=="glass",phase,blend,coffeePulse);
            statusRasterSeconds+=Stopwatch.GetElapsedTime(rasterStarted).TotalSeconds;
            statusItem.Update(bitmap,tooltip);
        }
        else statusItem.UpdateTooltip(tooltip);
        if(statusItem.Installed&&statusMotion.NeedsFrames(monotonic))statusAnimationTimer.Start();else statusAnimationTimer.Stop();
    }

    private void ChangeDisplayMode(string mode)
    {
        var settings=engine.Data.Settings;
        if(settings.DisplayMode==mode)return;
        var previousMode=settings.DisplayMode;var previousX=settings.WindowX;var previousY=settings.WindowY;
        if(settings.DisplayMode=="floating") { settings.WindowX=Left;settings.WindowY=Top; }
        settings.DisplayMode=mode;
        // Commit before removing the current window/entry. On failure, keep the
        // actual UI and selector aligned with the mode already on disk.
        if(!Persist())
        {
            settings.DisplayMode=previousMode;settings.WindowX=previousX;settings.WindowY=previousY;
            var wasLoading=loadingPreferences;loadingPreferences=true;
            try{DisplayPicker.SelectedItem=DisplayPicker.Items.Cast<System.Windows.Controls.ComboBoxItem>().Single(item=>item.Tag?.ToString()==previousMode);}
            finally{loadingPreferences=wasLoading;}
            ApplyMaterial();Render();return;
        }
        if(!ApplyDisplayMode())
        {
            settings.DisplayMode="floating";loadingPreferences=true;
            DisplayPicker.SelectedIndex=0;loadingPreferences=false;
            ApplyDisplayMode();ShowError("无法创建菜单栏入口，已保留悬浮窗。");
            Persist();
        }
        ApplyMaterial();Render();
    }

    private bool ApplyDisplayMode()
    {
        if(statusItem is null)return true;
        // A queued mode selection can arrive before the next fullscreen poll.
        // Refresh at the presentation boundary before any floating HWND is shown.
        if(!smoke)RefreshFullscreenAvoidance();
        CloseRecognitionPicker();DisplayPicker.IsDropDownOpen=false;focusReturn.Stop();
        if(IsMenuBar)
        {
            tray.Visible=false;restoreAfterFullscreen=false;
            HideMenuPanel();
            UpdateMenuTime();
            if(!statusItem.SetVisible(true))return false;
            UpdateMenuTime();return true;
        }
        statusAnimationTimer.Stop();statusItem.SetVisible(false);statusTimeKey="";tray.Visible=false;menuPanelOpen=false;
        Topmost=fullscreenSuppressed?false:engine.Data.Settings.Topmost;
        isExpanded=false;if(!applicationChooserOpen)ShowPage("home",false);SettleNavigation();
        Left=engine.Data.Settings.WindowX??SystemParameters.WorkArea.Left+(SystemParameters.WorkArea.Width-Width)/2;
        Top=engine.Data.Settings.WindowY??SystemParameters.WorkArea.Top;
        if(fullscreenSuppressed){restoreAfterFullscreen=true;UpdateTrayPresence();return true;}
        var activate=ShowActivated;try{ShowActivated=false;Show();ClampToScreen();}finally{ShowActivated=activate;}
        UpdateTrayPresence();return true;
    }

    private void ToggleMenuPanel()
    {
        if(!IsMenuBar)return;
        if(menuPanelOpen&&IsVisible){HideMenuPanel();return;}
        OpenMenuPanel();
    }
    private void OpenMenuPanel()
    {
        if(!smoke)RefreshFullscreenAvoidance();
        if(fullscreenSuppressed || suspended)return;
        menuPanelOpen=true;isExpanded=true;
        if(!applicationChooserOpen)ShowPage("home",false);
        SettleNavigation();ApplyMaterial();
        Topmost=engine.Data.Settings.Topmost;
        Show();PositionMenuPanel();RestoreOwnedPopups();Activate();focusReturn.Stop();
    }
    private void HideMenuPanel()
    {
        menuPanelOpen=false;CloseRecognitionPicker();focusReturn.Stop();SuppressOwnedPopups();Hide();
        isExpanded=false;if(!applicationChooserOpen)ShowPage("home",false);SettleNavigation();
    }
    private void MenuFocusReturn()
    {
        Dispatcher.BeginInvoke(()=>
        {
            if(!IsMenuBar || !menuPanelOpen || IsActive || HasOpenPicker())return;
            // The status-item callback owns its toggle. Do not close first and
            // turn the same click into a reopen when focus leaves for Explorer.
            if(statusItem?.Bounds is {} anchor && anchor.Contains(Forms.Cursor.Position))return;
            HideMenuPanel();
        },DispatcherPriority.Background);
    }

    public static Rectangle MenuPanelFrame(System.Drawing.Size size,Rectangle anchor,Rectangle work)
    {
        var x=Math.Clamp(anchor.Left+(anchor.Width-size.Width)/2,work.Left,Math.Max(work.Left,work.Right-size.Width));
        var y=anchor.Top-size.Height-8;
        if(y<work.Top)y=anchor.Bottom+8;
        y=Math.Clamp(y,work.Top,Math.Max(work.Top,work.Bottom-size.Height));
        return new(x,y,size.Width,size.Height);
    }
    private void PositionMenuPanel()
    {
        var cursor=Forms.Cursor.Position;var anchor=statusItem?.Bounds??new Rectangle(cursor.X,cursor.Y,1,1);
        var work=Forms.Screen.FromRectangle(anchor).WorkingArea;
        var window=new WindowInteropHelper(this).Handle;
        if(!WindowsActivity.GetWindowRect(window,out var rect))return;
        var frame=MenuPanelFrame(new(rect.Right-rect.Left,rect.Bottom-rect.Top),anchor,work);
        WindowsActivity.SetWindowPos(window,0,frame.X,frame.Y,0,0,0x0015);
    }
}
