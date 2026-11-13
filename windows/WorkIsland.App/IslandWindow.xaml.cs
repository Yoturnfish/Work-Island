using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WorkIsland.Core;
using Forms = System.Windows.Forms;

namespace WorkIsland.App;

public partial class IslandWindow : Window
{
    private readonly AtomicStore store;
    private readonly IStartupRegistration startupRegistration;
    private readonly TrackingEngine engine;
    private readonly IRecognitionSource recognitionSource;
    private readonly IApplicationPicker applicationPicker;
    private readonly IFullscreenSource fullscreenSource;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Forms.NotifyIcon tray;
    private System.Drawing.Icon? ownedTrayIcon;
    private readonly bool smoke;
    private readonly string? smokeOutput;
    private readonly bool nativeSmoke;
    private readonly bool restartSmoke;
    private readonly bool visualReview;
    private bool exiting;
    private bool suspended;
    private bool sessionLocked;
    private bool systemSleeping;
    private string? lastSaved;
    private string? lastSaveError;
    private bool normalExitPrepared;
    private long renderCalls;
    public bool SmokeFailed { get; private set; }
    public bool FidelityReview { get; set; }
    public bool MotionReview { get; set; }
    private readonly SolidColorBrush statusBrush = new(Color.FromRgb(255, 149, 0));
    private string visualState = "";
    private bool isExpanded;
    private FrameworkElement? activePage;
    private int transitionSerial;
    private readonly Dictionary<FrameworkElement, Transform> restingTransforms = [];
    private readonly Dictionary<FrameworkElement, TranslateTransform> transitionMovements = [];
    private ClockGroup? rootTransition, pageTransition;
    private int rootTransitionSerial;
    private IslandSurfaces? surfaces;

    public IslandWindow(AtomicStore store, AppState state, bool smoke, string? smokeOutput, bool nativeSmoke = false, bool restartSmoke = false, bool visualReview = false,IStartupRegistration? startupRegistration=null,IRecognitionSource? recognitionSource=null,IApplicationPicker? applicationPicker=null,IFullscreenSource? fullscreenSource=null)
    {
        InitializeComponent();
        // Direct-window raster probes start on the software reference renderer.
        // Ordinary Program startup selects the default live renderer below via
        // EnableLiveForeground, without any whole-window snapshot handoff.
        SourceInitialized+=(_,_)=>((HwndSource)PresentationSource.FromVisual(this)!).CompositionTarget.RenderMode=RenderMode.SoftwareOnly;
        CaptureInterfaceLabels();
        VersionLabel.Text = "v" + typeof(IslandWindow).Assembly.GetName().Version!.ToString(3);
        this.store = store; this.smoke = smoke; this.smokeOutput = smokeOutput;
        this.recognitionSource=recognitionSource??new WindowsRecognitionSource();
        this.applicationPicker=applicationPicker??new WindowsApplicationPicker(Text);
        this.fullscreenSource=fullscreenSource??new WindowsFullscreenSource();
        this.startupRegistration=startupRegistration ?? new WindowsStartupRegistration(Path.Combine(AppContext.BaseDirectory,"WorkIsland.exe"),Path.GetDirectoryName(store.Path)!);
        this.nativeSmoke = nativeSmoke;
        this.restartSmoke = restartSmoke;
        this.visualReview = visualReview;
        engine = new(state);
        ColorProfilesService.Initialize(state.Settings);
        ApplyScale();
        state.Settings.Topmost = true;
        Topmost = true;
        LoadSettingsInputs(); RebuildRules();
        InitializeVisualPreferences();
        CompactPanel.IsVisibleChanged+=(_,_)=>{if(!exiting){if(!CompactPanel.IsVisible)StopCompactInk(true);UpdateStatusPulse(engine.Current.State,PresentationReduced);}};
        InitializeOnboarding();
        PreviewKeyDown+=RecognitionPickerKey;
        TitleLabel.SizeChanged += (_, _) => FitCompactTitle();
        MessageLabel.Text = store.RecoveryMessage ?? "";
        MessageLabel.Visibility = string.IsNullOrEmpty(MessageLabel.Text) ? Visibility.Collapsed : Visibility.Visible;
        RefreshLoginStartupState(false);
        ownedTrayIcon=new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory,"Assets","WorkIsland.ico"));
        tray = new Forms.NotifyIcon { Icon = ownedTrayIcon, Text = "Work Island · 今日目标 4h", Visible = false };
        Icon=BitmapFrame.Create(new Uri(Path.Combine(AppContext.BaseDirectory,"Assets","WorkIsland.ico")));
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示 Work Island", null, (_, _) => Dispatcher.Invoke(ShowIsland));
        menu.Items.Add("暂停 / 恢复", null, (_, _) => Dispatcher.Invoke(() => TogglePause(null, new RoutedEventArgs())));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(() => ExitIsland(null, new RoutedEventArgs())));
        tray.ContextMenuStrip = menu;
        menu.Opening += TrayMenuOpening;
        RefreshTrayLanguage();
        menu.Closed += (_, _) => Dispatcher.BeginInvoke(() => { if (!IsActive && !smoke) BeginFocusReturn(); });
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowIsland);
        timer.Tick += (_, _) => Tick();
        fullscreenTimer.Tick += (_, _) => RefreshFullscreenAvoidance();
        SystemEvents.PowerModeChanged += PowerModeChanged;
        SystemEvents.TimeChanged += TimeChanged;
        SystemEvents.SessionSwitch += SessionSwitch;
        SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
        Loaded += (_, _) =>
        {
            if(!smoke)WindowsActivity.Attach(new WindowInteropHelper(this).Handle);
            Left = state.Settings.WindowX ?? SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - Width) / 2;
            Top = state.Settings.WindowY ?? SystemParameters.WorkArea.Top;
            ClampToScreen(); Tick(); timer.Start();
            surfaces = new IslandSurfaces(this); ApplyMaterial(); ApplyLanguage();
            InitializeStatusItem();
            UpdateTrayPresence();
            if(IsMenuBar && !ApplyDisplayMode())
            { engine.Data.Settings.DisplayMode="floating";Persist();ApplyDisplayMode(); }
            if (!smoke)
            {
                RefreshFullscreenAvoidance();
                if (startupSuppressed) { startupSuppressed = false; if (!fullscreenSuppressed) Topmost = engine.Data.Settings.Topmost; ApplyWindowOpacity(); }
                fullscreenTimer.Start();
                if(!state.Settings.HasCompletedOnboarding)BeginOnboarding();
            }
            if (smoke) Dispatcher.BeginInvoke(async () => { if (StabilityReview) await RunStabilityReview(); else if (MotionReview) await RunMotionReview(); else if (FidelityReview) await RunFidelityReview(); else if (visualReview) await RunVisualReview(); else if (restartSmoke) await RunRestartSmoke(); else await RunSmoke(); }, DispatcherPriority.ApplicationIdle);
        };
        Closing += (_, e) => { if (!exiting) { e.Cancel = true; HideIsland(null, new RoutedEventArgs()); } else DisposeNativeForeground(); };
        Closed += (_, _) =>
        {
            CloseRecognitionPicker();StopMaterialMotion(true);StopOnboardingMotion();StopOnboardingRootMotion();StopStateTransition();++pulseSerial;
            textBackdropSampler?.Dispose();textBackdropSampler=null;StopCompactInk(true);
            statusPulseClock?.Controller?.Remove();statusPulseClock=null;
            var scale=(ScaleTransform)StatusIcon.RenderTransform;scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);scale.BeginAnimation(ScaleTransform.ScaleYProperty,null);
            StopReminderMotion();if(!smoke)WindowsActivity.Detach();
            statusAnimationTimer.Stop();statusItem?.Dispose();statusItem=null;statusArtwork.Dispose();
            timer.Stop(); fullscreenTimer.Stop(); SystemEvents.PowerModeChanged -= PowerModeChanged; SystemEvents.TimeChanged -= TimeChanged;
            SystemEvents.SessionSwitch -= SessionSwitch;
            SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
            tray.Visible = false; tray.Dispose();
            ownedTrayIcon?.Dispose();ownedTrayIcon=null;
        };
    }
    private void Tick()
    {
        if (suspended || smoke && timer.IsEnabled) return;
        RefreshActivityObservation();
        Persist(); Render();
    }
    private void RefreshActivityObservation()
    {
        var input = WindowsActivity.Snapshot(!engine.Data.Settings.Paused && engine.Data.Settings.Websites.Any(r=>r.IsEnabled),engine.Data.Settings.AwayThreshold);
        engine.Zone = TimeZoneInfo.Local;
        ObserveActivity(input);
    }
    private void ObserveActivity(ActivityInput input) => engine.Observe(DateTimeOffset.Now, input,
        System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency);
    private bool Persist()=>SaveState(false);
    private bool SaveState(bool force)
    {
        try
        {
            lastSaved=store.SaveIfChanged(engine.Data,lastSaved,force);
            lastSaveError=null;ResolveRecognitionSaveFeedback();ResolveProfileSaveFeedback();ResolveSaveFeedback();return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            var repeated=RememberSaveError(error);
            if(force||!repeated)PresentSaveFailure(force);return false;
        }
    }
    private bool RememberSaveError(Exception error)
    {
        var message="保存失败，计时暂留内存："+error.Message;var repeated=message==lastSaveError;lastSaveError=message;return repeated;
    }
    private void Render()
    {
        SettleOnboardingIfReduced();
        profilePreviewMotion?.Invoke();
        renderCalls++;
        var now = DateTimeOffset.Now;
        var seconds = engine.TodaySeconds(now);
        TimeLabel.Text = DurationText.Compact(seconds,engine.Data.Settings.DurationStyle);
        TitleLabel.Text = string.IsNullOrWhiteSpace(engine.Data.Settings.DisplayTitle) ? Text("屏幕工作时间") : engine.Data.Settings.DisplayTitle;
        TitleLabel.Opacity = 1;
        var reduced = PresentationReduced;
        RefreshAdaptiveTextContrast();
        PresentState(engine.Current.State,reduced);
        TitleLabel.Foreground=TitleEditor.Foreground=compactTitleInk;TimeLabel.Foreground=statusBrush;
        RenderVisualExtras(now, reduced);
        FitCompactTitle();
        AlignCompactTypography();
        GoalProgress.Update(engine.Progress(now), visualState, engine.Data.Settings.ProgressStyle, statusBrush, reduced,
            engine.Data.Settings.IslandScale < 0.65 ? 10 : engine.Data.Settings.IslandScale < 0.82 ? 16 : 30, UnfinishedAwayBreathing(now));
        PauseActionIcon.Text = engine.Data.Settings.Paused ? "▶" : "Ⅱ";
        PauseNavigationGlyph.Symbol = engine.Data.Settings.Paused ? "play.fill" : "pause.fill";
        PauseActionText.Text = Text(engine.Data.Settings.Paused ? "继续计时" : "暂停计时");
        DurationButton.Content = engine.Data.Settings.DurationStyle == "hoursMinutes" ? "小时 / 分钟" : "分钟 / 秒";
        var trayTitle=TitleLabel.Text;trayTitle=trayTitle[..Math.Min(20,trayTitle.Length)];
        tray.Text = $"Work Island · {trayTitle} · {FormatDuration(seconds)} / {engine.Data.Settings.GoalSeconds / 3600:0.#}h";
        UpdateTrayPresence();
        UpdateMenuTime();
        var localDate = DateOnly.ParseExact(LogicalDay.Key(now, engine.Zone, engine.Data.Settings.DayResetMinutes), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        HistoryLabel.Text = string.Join("\n", Enumerable.Range(0, 7).Select(i =>
        {
            var key = localDate.AddDays(-i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return key + "     " + FormatDuration(engine.Data.Sessions.Where(s => s.DayKey == key).Sum(s => s.EffectiveSeconds));
        }));
        QueueNativeForeground();
    }
    private static string FormatDuration(double seconds) => DurationText.Clock(seconds);
    private bool PresentationReduced=>suspended||fullscreenSuppressed||!IsVisible||OnboardingActive||IsMenuBar&&!menuPanelOpen||engine.Data.Settings.ReduceMotion||!SystemParameters.ClientAreaAnimation;
    private void UpdateTrayPresence()
    {
        // A visible floating island is already the entry. Only hidden floating
        // mode needs a recovery icon; it must not imitate a second state item.
        tray.Visible=!exiting&&!IsMenuBar&&!IsVisible;
    }
    private void FitCompactTitle()
    {
        if (TitleLabel.ActualWidth <= 0 || TitleLabel.Visibility != Visibility.Visible) return;
        var typeface = new Typeface(TitleLabel.FontFamily, TitleLabel.FontStyle, TitleLabel.FontWeight, TitleLabel.FontStretch);
        var text = new FormattedText(TitleLabel.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 13,
            Brushes.White,null,TextOptions.GetTextFormattingMode(TitleLabel),VisualTreeHelper.GetDpi(this).PixelsPerDip);
        TitleLabel.FontSize = 13 * RuntimeCompat.Clamp(TitleLabel.ActualWidth / Math.Max(1, text.WidthIncludingTrailingWhitespace), .65, 1);
        compactTitleInkWidth=Math.Min(TitleLabel.ActualWidth,text.WidthIncludingTrailingWhitespace*TitleLabel.FontSize/13);
    }
    private void AlignCompactTypography()
    {
        if(TitleLabel.Visibility!=Visibility.Visible){TimeLabel.RenderTransform=Transform.Identity;return;}
        FormattedText Measure(TextBlock label,string value,double size)=>new(value,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,
            new Typeface(label.FontFamily,label.FontStyle,label.FontWeight,label.FontStretch),size,Brushes.White,null,TextOptions.GetTextFormattingMode(label),VisualTreeHelper.GetDpi(this).PixelsPerDip);
        // A small, fixed increase from the original 13 DIP. Align the visible
        // centers without inflating Latin capitals to the full CJK glyph height.
        TimeLabel.FontSize=14;
        // Editing, language changes and a wider time string invalidate arrange.
        // Measure the restored title only after its actual layout is available.
        CompactPanel.UpdateLayout();
        var title=Measure(TitleLabel,TitleLabel.Text,TitleLabel.FontSize);
        var time=Measure(TimeLabel,TimeLabel.Text,TimeLabel.FontSize);
        var leftInk=title.BuildGeometry(new Point()).Bounds;
        var rightInk=time.BuildGeometry(new Point()).Bounds;
        // Include actual arranged text origins; center alignment cannot assume
        // equal font line heights. Quantize the correction to screen pixels.
        var leftOrigin=TitleLabel.TranslatePoint(new Point(),CompactPanel);
        var rightOrigin=TimeLabel.TranslatePoint(new Point(),CompactPanel);
        var previous=TimeLabel.RenderTransform.Value.OffsetY;
        var delta=leftOrigin.Y+leftInk.Y+leftInk.Height/2-(rightOrigin.Y-previous+rightInk.Y+rightInk.Height/2);
        var dpi=VisualTreeHelper.GetDpi(this).DpiScaleY;
        var aligned=Math.Round(delta*dpi)/dpi;
        if(Math.Abs(previous-aligned)>.001)TimeLabel.RenderTransform=new TranslateTransform(0,aligned);
    }
    private void TogglePause(object? sender, RoutedEventArgs e)
    {
        if (!smoke) Tick();
        engine.Data.Settings.Paused = !engine.Data.Settings.Paused;
        ObserveActivity(WindowsActivity.Snapshot()); Persist(); Render();
    }
    private void ToggleExpanded(object? sender, RoutedEventArgs e)
    {
        if(OnboardingActive)return;
        if(IsMenuBar && isExpanded){HideMenuPanel();return;}
        isExpanded = !isExpanded;
        if (isExpanded) { ShowPage("home", false); Transition(CompactPanel, DetailsPanel, true); }
        else Transition(DetailsPanel, CompactPanel, false);
        ApplyMaterial();
        UpdateLayout(); ClampToScreen();
    }
    private void StopTransition(bool root)
    {
        var clock = root ? rootTransition : pageTransition;
        if (root) rootTransition = null; else pageTransition = null;
        clock?.Controller?.Remove();
        if(root&&clock is not null)
        {
            GlassPageWash.ApplyAnimationClock(OpacityProperty,null);
            GlassPageWash.Opacity=engine.Data.Settings.BackgroundStyle=="glass"&&isExpanded?1:0;
            GlassPageWash.Visibility=GlassPageWash.Opacity>0?Visibility.Visible:Visibility.Collapsed;
        }
        foreach (var element in transitionMovements.Keys.Where(element =>
            (element == CompactPanel || element == DetailsPanel) == root).ToArray())
        {
            transitionMovements[element].BeginAnimation(TranslateTransform.XProperty, null);
            element.BeginAnimation(OpacityProperty, null);
            element.RenderTransform = restingTransforms[element];
            element.Opacity = 1;
            transitionMovements.Remove(element);
        }
    }
    private void Transition(FrameworkElement outgoing, FrameworkElement incoming, bool forward, Action? completed = null)
    {
        var root = outgoing == CompactPanel || outgoing == DetailsPanel;
        var continueNative=root&&nativePulseEnabled&&nativeForegroundActive&&NativeStateFrameAllowed;
        if(!continueNative)SuspendNativeForeground();
        var serial = root ? ++rootTransitionSerial : ++transitionSerial;
        // Capture the presentation before removing the old clocks, so retargeting never snaps.
        var outgoingX = transitionMovements.TryGetValue(outgoing, out var oldOutgoing) ? oldOutgoing.X : 0;
        var outgoingAlpha = outgoing.Opacity;
        var returning = transitionMovements.TryGetValue(incoming, out var oldIncoming);
        var incomingX = returning ? oldIncoming!.X : 0;
        var incomingAlpha = returning ? incoming.Opacity : 0;
        var washFrom=GlassPageWash.Opacity;
        StopTransition(root);
        foreach (var element in new[] { outgoing, incoming })
        {
            if (!restingTransforms.ContainsKey(element)) restingTransforms[element] = element.RenderTransform;
        }
        incoming.Visibility = Visibility.Visible;
        UpdateLayout();
        if (engine.Data.Settings.ReduceMotion || !SystemParameters.ClientAreaAnimation)
        { outgoing.Visibility = Visibility.Collapsed; incoming.Opacity = 1; completed?.Invoke();QueueNativeForeground(); return; }
        if (!returning) incomingX = forward ? incoming.ActualWidth : -incoming.ActualWidth;
        TransformGroup Moving(FrameworkElement element, TranslateTransform movement)
        {
            var group = new TransformGroup();
            group.Children.Add(restingTransforms[element]); group.Children.Add(movement);
            transitionMovements[element] = movement;
            return group;
        }
        var translate = new TranslateTransform(incomingX, 0);
        incoming.RenderTransform = Moving(incoming, translate); incoming.Opacity = incomingAlpha;
        var outgoingTranslate = new TranslateTransform(outgoingX, 0);
        outgoing.RenderTransform = Moving(outgoing, outgoingTranslate); outgoing.Opacity = outgoingAlpha;
        var timeline = new ParallelTimeline { Duration = TimeSpan.FromSeconds(.24) };
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut }; easing.Freeze();
        void AddTrack(double from, double to)
        {
            var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(.24)) { EasingFunction = easing };
            timeline.Children.Add(animation);
        }
        AddTrack(incomingX, 0);
        AddTrack(outgoingX, forward ? -outgoing.ActualWidth : outgoing.ActualWidth);
        AddTrack(incomingAlpha, 1);
        AddTrack(outgoingAlpha, 0);
        var animateWash=root&&engine.Data.Settings.BackgroundStyle=="glass";
        if(animateWash)AddTrack(washFrom,forward?1:0);
        var clock = (ClockGroup)timeline.CreateClock(true);
        translate.ApplyAnimationClock(TranslateTransform.XProperty, (AnimationClock)clock.Children[0]);
        outgoingTranslate.ApplyAnimationClock(TranslateTransform.XProperty, (AnimationClock)clock.Children[1]);
        incoming.ApplyAnimationClock(OpacityProperty, (AnimationClock)clock.Children[2]);
        outgoing.ApplyAnimationClock(OpacityProperty, (AnimationClock)clock.Children[3]);
        if(animateWash){GlassPageWash.Visibility=Visibility.Visible;GlassPageWash.ApplyAnimationClock(OpacityProperty,(AnimationClock)clock.Children[4]);}
        clock.Completed += (_, _) =>
        {
            if (serial != (root ? rootTransitionSerial : transitionSerial)) return;
            outgoing.Visibility = Visibility.Collapsed;
            StopTransition(root);
            completed?.Invoke();
            QueueNativeForeground();
        };
        if (root) rootTransition = clock; else pageTransition = clock;
        clock.Controller!.Begin();
        if(continueNative)BeginNativeStateTransition();
    }
    private void ShowPage(string page, bool animate = true)
    {
        if(page!="recognition")CloseRecognitionPicker();
        var previous = activePage;
        var next = page switch { "settings" => SettingsPage, "recognition" => RecognitionPage, "history" => HistoryPage, _ => (FrameworkElement)NavigationHome };
        if (!animate) { ++transitionSerial; StopTransition(false); }
        // Keep the shared page shell visible until the outgoing subpage has finished sliding.
        var leavingSubpage = page == "home" && previous is not null && previous != NavigationHome && animate;
        NavigationHome.Visibility = page == "home" ? Visibility.Visible : Visibility.Collapsed;
        Subpage.Visibility = page == "home" && !leavingSubpage ? Visibility.Collapsed : Visibility.Visible;
        foreach (var panel in new[] { SettingsPage, RecognitionPage, HistoryPage }) panel.Visibility = ReferenceEquals(panel, next) ? Visibility.Visible : Visibility.Collapsed;
        activePage = next;
        PageScroller.VerticalScrollBarVisibility=ScrollBarVisibility.Visible;
        PageScroller.ScrollToTop();
        if (animate && previous is not null && previous != next)
        {
            previous.Visibility = Visibility.Visible;
            Transition(previous, next, page != "home", leavingSubpage ? () =>
            {
                if (ReferenceEquals(activePage, NavigationHome)) Subpage.Visibility = Visibility.Collapsed;
            } : null);
        }
    }
    private void OpenSettings(object sender, RoutedEventArgs e) { RefreshLoginStartupState(false);ShowPage("settings");RevealLoginFeedback();if (!smoke) Activate(); }
    private void OpenHistory(object sender, RoutedEventArgs e) { historyMode="list"; historySignature=""; RefreshHistory(); ShowPage("history"); }
    private void OpenRecognition(object sender, RoutedEventArgs e) { RuleRows.Visibility=WebsiteRows.Visibility=Visibility.Visible; RuleInputRow.Visibility=WebsiteInputRow.Visibility=Visibility.Collapsed; RecognitionMessage(""); RefreshRecognitionHeaders();ShowPage("recognition"); }
    private void BackToNavigation(object? sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(activePage, HistoryPage) && historyMode != "list") { historyMode="list"; historySignature=""; RefreshHistory(); PageScroller.VerticalScrollBarVisibility=ScrollBarVisibility.Visible; PageScroller.ScrollToTop(); }
        else ShowPage("home");
    }
    private void ChangeProgressStyle(object? sender, RoutedEventArgs e) { engine.Data.Settings.ProgressStyle = engine.Data.Settings.ProgressStyle == "matrix" ? "waveform" : "matrix"; Persist(); Render(); }
    private void ChangeDurationStyle(object? sender, RoutedEventArgs e) { engine.Data.Settings.DurationStyle = engine.Data.Settings.DurationStyle == "hoursMinutes" ? "minutesSeconds" : "hoursMinutes"; Persist(); Render(); }
    private void ApplyScale()
    {
        SuspendNativeForeground();
        var scale = engine.Data.Settings.IslandScale;
        Width = 344 * scale; Height = 92 * scale;
        OuterBorder.CornerRadius = StateWash.CornerRadius = Outline.CornerRadius = GlassPageWash.CornerRadius = new CornerRadius(26 * scale);
        PageHost.Clip = new RectangleGeometry(new Rect(0, 0, Width, Height), 26 * scale, 26 * scale);
        DetailsPanel.LayoutTransform = new ScaleTransform(scale, scale);
        if(onboardingRoot is not null)onboardingRoot.LayoutTransform=new ScaleTransform(scale,scale);
        var adaptive = scale < 0.82;
        CompactPanel.Margin = new Thickness(adaptive ? 14 : 18, 0, adaptive ? 14 : 18, 0);
        CompactPanel.ColumnDefinitions[0].Width = new GridLength(adaptive ? 26 : 34);
        CompactPanel.ColumnDefinitions[1].Width = new GridLength(adaptive ? 9 : 12);
        ExpandButton.Width = ExpandButton.Height = adaptive ? 26 : 34;
        OrbContent.Width = OrbContent.Height = adaptive ? 26 : 34;
        ExpandButton.Clip = null;
        GoalProgress.Height = adaptive ? 14 : 24;
        GoalProgress.Margin = new Thickness(0, adaptive ? 6 : 2, 0, 0);
        TimeLabel.FontSize = adaptive ? 12 : 14;
        Grid.SetColumn(TimeLabel, adaptive ? 0 : 1);
        TitleLabel.Visibility = adaptive ? Visibility.Collapsed : Visibility.Visible;
        surfaces?.UpdateMaterialGlass(MaterialGlassAmount);
    }
    private void TitleClick(object sender, MouseButtonEventArgs e) { if (e.ClickCount != 3) return; e.Handled = true; SuspendNativeForeground(); TitleEditor.Text = engine.Data.Settings.DisplayTitle; TitleLabel.Visibility = Visibility.Collapsed; TitleEditor.Visibility = Visibility.Visible; TitleEditor.Focus(); }
    private void TitleKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { engine.Data.Settings.DisplayTitle = ColorProfilesService.NormalizeTitle(TitleEditor.Text); Persist(); CancelTitleEdit(null, null!); Render(); } else if (e.Key == Key.Escape) CancelTitleEdit(null, null!); }
    private void CancelTitleEdit(object? sender, KeyboardFocusChangedEventArgs e) { TitleEditor.Visibility = Visibility.Collapsed; TitleLabel.Visibility = Visibility.Visible;if(nativeForegroundEnabled)AlignCompactTypography();QueueNativeForeground(); }
    private void HideIsland(object? sender, RoutedEventArgs e) { restoreAfterFullscreen = false; SuppressOwnedPopups(); if(IsMenuBar){HideMenuPanel();return;} CloseRecognitionPicker(); Hide(); }
    private void ShowIsland()
    {
        if(IsMenuBar){OpenMenuPanel();return;}
        if (!smoke) RefreshFullscreenAvoidance();
        if (fullscreenSuppressed) { restoreAfterFullscreen = true; return; }
        Show(); RestoreOwnedPopups(); Activate(); ClampToScreen();
    }
    internal void OpenTrayMenuForReview() => tray.ContextMenuStrip!.Show(Forms.Cursor.Position);
    internal object InteractionSnapshot() => new
    {
        expanded = isExpanded, activePage = activePage?.Name, active = IsActive, visible = IsVisible,
        compactVisible = CompactPanel.IsVisible, navigationVisible = DetailsPanel.IsVisible,
        x = Left, y = Top, width = ActualWidth, height = ActualHeight,
        scale = engine.Data.Settings.IslandScale, paused = engine.Data.Settings.Paused,
        backdrop = surfaces?.BackdropVisible, shadow = surfaces?.ShadowVisible,
        title = engine.Data.Settings.DisplayTitle, background = engine.Data.Settings.BackgroundStyle,
        focusReturnPending = focusReturn.IsEnabled
    };
    private void ExitIsland(object? sender, RoutedEventArgs e)
    {
        if(PrepareNormalExit())CompleteNormalExit();
    }
    internal bool PrepareNormalExit()
    {
        if(exiting || normalExitPrepared)return true;
        if(applicationChooserOpen){lastSaveError="请先关闭应用选择窗口，再退出。";return false;}
        if (!smoke) Tick();
        if(!IsMenuBar&&!OnboardingActive){engine.Data.Settings.WindowX = Left; engine.Data.Settings.WindowY = Top;}
        if(!SaveState(true))
        {
            if(!IsVisible&&!fullscreenSuppressed&&!suspended){ShowIsland();PresentSaveFailure(true);}
            return false;
        }
        normalExitPrepared=true;timer.Stop();return true;
    }
    internal void CompleteNormalExit(){if(!normalExitPrepared)return;exiting=true;Close();}
    internal string? NormalExitError=>lastSaveError;
    private string SaveFailureMessage=>engine.Data.Settings.Language=="en"
        ? "Save failed; records remain in memory. Check storage, then retry exit."
        : "保存失败，记录暂留内存。请检查存储位置后重试退出。";
    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if(IsMenuBar)return;
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var source = e.OriginalSource as DependencyObject;
        while (source is not null && source != PageHost)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.TextBoxBase
                or System.Windows.Controls.Primitives.Thumb or System.Windows.Controls.Primitives.ScrollBar or Slider) return;
            source = VisualTreeHelper.GetParent(source);
        }
        try { DragMove(); } catch (InvalidOperationException) { return; }
        ClampToScreen(); if(!OnboardingActive){engine.Data.Settings.WindowX = Left; engine.Data.Settings.WindowY = Top; Persist();}
    }
    private void ClampToScreen()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !WindowsActivity.GetWindowRect(hwnd, out var rect)) return;
        var work = Forms.Screen.FromHandle(hwnd).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        MaxHeight = Math.Max(140, (work.Height - 16) / dpi.DpiScaleY);
        // Native coordinates avoid comparing DIPs with physical pixels across monitors.
        var x = RuntimeCompat.Clamp(rect.Left, work.Left, Math.Max(work.Left, work.Right - (rect.Right - rect.Left)));
        var y = RuntimeCompat.Clamp(rect.Top, work.Top, Math.Max(work.Top, work.Bottom - (rect.Bottom - rect.Top)));
        WindowsActivity.SetWindowPos(hwnd, 0, x, y, 0, 0, 0x0015);
    }
    private void LoadSettingsInputs()
    {
        GoalInput.Text = (engine.Data.Settings.GoalSeconds / 3600).ToString("R", CultureInfo.CurrentCulture);
        IdleInput.Text = engine.Data.Settings.AwayThreshold.ToString("R", CultureInfo.CurrentCulture);
        ResetInput.Minutes = engine.Data.Settings.DayResetMinutes;
    }
    private void SaveSettings(object? sender, RoutedEventArgs e)
    {
        if (!double.TryParse(GoalInput.Text, out var goal) || !double.TryParse(IdleInput.Text, out var idle))
        { ShowError("目标须为 0–24 小时（大于 0）；空闲阈值 1–3600 秒。"); return; }
        SaveTrackingPreferences(goal*3600,idle);
    }
    private void SaveTrackingPreferences(double goalSeconds,double idle)
    {
        if(!RuntimeCompat.IsFinite(goalSeconds)||goalSeconds<=0||goalSeconds>24*3600
            ||!RuntimeCompat.IsFinite(idle)||idle<1||idle>3600)
        { ShowError("目标须为 0–24 小时（大于 0）；空闲阈值 1–3600 秒。"); return; }
        // Credit the last observation under the old threshold. A failed edit
        // must not discard that work or replace the paused/current decision.
        if(!smoke&&!suspended)RefreshActivityObservation();
        var previousGoal=engine.Data.Settings.GoalSeconds;var previousIdle=engine.Data.Settings.AwayThreshold;
        engine.Data.Settings.GoalSeconds = goalSeconds; engine.Data.Settings.AwayThreshold = idle;
        if(!Persist())
        {
            engine.Data.Settings.GoalSeconds=previousGoal;engine.Data.Settings.AwayThreshold=previousIdle;
            RestoreTrackingPreferenceControls();ShowPreferenceSaveFailure();Render();return;
        }
        RestoreTrackingPreferenceControls();PreferenceSaveSucceeded();Render();
        MessageLabel.Text=engine.Data.Settings.Language=="en"?"Settings saved.":"设置已保存。";
    }
    private void AddRule(object? sender, RoutedEventArgs e)
    {
        var name = RuleInput.Text.Trim();
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
        if (name.Length <= 4 || name.Length > 120 || name.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0 || name.Any(char.IsControl))
        { RecognitionMessage("请输入有效的进程名，例如 code.exe。"); return; }
        if (engine.Data.Settings.Rules.Any(r => r.Identifier.Equals(name, StringComparison.OrdinalIgnoreCase))) { RecognitionMessage("这个应用已在列表中。勾选它即可启用。"); return; }
        engine.Data.Settings.Rules.Add(new(name, name[..^4])); RuleInput.Clear(); var saved=PersistRecognitionChange(); RebuildRules(); if(saved)RecognitionMessage("已添加 " + name);
    }
    private void RebuildRules()
    {
        RuleRows.Children.Clear();
        foreach (var rule in engine.Data.Settings.Rules.ToArray())
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var check = new CheckBox { Content = RuleName(rule.DisplayName), IsChecked = rule.IsEnabled, FontSize = 12, Style=(Style)FindResource("RuleCheckStyle") };
            check.Click += (_, _) => { var index = engine.Data.Settings.Rules.IndexOf(rule); if (index >= 0) { engine.Data.Settings.Rules[index] = rule with { IsEnabled = check.IsChecked == true }; PersistRecognitionChange(); RebuildRules(); } };
            var remove = new Button { Content = VectorIcon.Make("trash", new SolidColorBrush(Color.FromArgb(173,255,255,255)), 12), Width = 18, Height = 24, Margin = new Thickness(6, 0, 0, 0) };
            remove.Click += (_, _) => { engine.Data.Settings.Rules.Remove(rule); PersistRecognitionChange(); RebuildRules(); };
            Grid.SetColumn(remove, 1); row.Children.Add(check); row.Children.Add(remove); RuleRows.Children.Add(row);
        }
        RebuildWebsites();
        RefreshRecognitionHeaders();
    }
    public void ShowError(string text) { if(OnboardingActive){OnboardingMessage(text);return;} if(ReferenceEquals(activePage,RecognitionPage)){RecognitionMessage(text);return;} saveFeedbackText=null;MessageLabel.Foreground=new SolidColorBrush(Color.FromRgb(255,183,85));MessageLabel.Text = text; MessageLabel.Visibility=Visibility.Visible; if (!isExpanded) ToggleExpanded(null, new RoutedEventArgs()); ShowPage("settings", false); RevealSettingsMessage(text); }
    private void PowerModeChanged(object sender, PowerModeChangedEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (e.Mode == PowerModes.Suspend) systemSleeping = true;
        else if (e.Mode == PowerModes.Resume) systemSleeping = false;
        else return;
        RefreshSuspension();
    });
    private void SessionSwitch(object sender, SessionSwitchEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (e.Reason == SessionSwitchReason.SessionLock) sessionLocked = true;
        else if (e.Reason == SessionSwitchReason.SessionUnlock) sessionLocked = false;
        else return;
        RefreshSuspension();
    });
    private void RefreshSuspension()
    {
        suspended = sessionLocked || systemSleeping;
        elapsedState=""; awaySince=null;
        engine.ResetObservation(); Persist();
        if (!suspended) Tick();
        else Render();
    }
    private void TimeChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        TimeZoneInfo.ClearCachedData();engine.Zone=TimeZoneInfo.Local;engine.ResetObservation();
        // Away duration and reminder scheduling also belong to the observation
        // period; a clock correction must not produce a fictitious away hour.
        elapsedState="";awaySince=null;
        ResetReminder(false);
    });

    private async Task RunSmoke()
    {
        var output = smokeOutput ?? Path.Combine(Path.GetDirectoryName(store.Path)!, "smoke");
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); checks.Add(text); }
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            timer.Stop();
            Check(IsVisible && new WindowInteropHelper(this).Handle != IntPtr.Zero, "real HWND created");
            Check(!tray.Visible, "visible island has no duplicate recovery tray entry");
            Check(engine.Data.Settings.GoalSeconds == 14400, "default goal 4h");
            SaveImage(Path.Combine(output, "collapsed.png"));
            Click(ExpandButton); await Task.Delay(300); Check(DetailsPanel.IsVisible, "navigation replaces timer at same size");
            var pixelTolerance = 1 / VisualTreeHelper.GetDpi(this).PixelsPerDip;
            Check(Math.Abs(Width - 344 * engine.Data.Settings.IslandScale) <= pixelTolerance && Math.Abs(Height - 92 * engine.Data.Settings.IslandScale) <= pixelTolerance,
                $"source window dimensions retained within physical pixel rounding ({Width:0.##} x {Height:0.##})");
            SaveImage(Path.Combine(output, "navigation.png"));
            for (var i = 0; i < 10; i++) { Click(PauseButton); Check(engine.Data.Settings.Paused == (i % 2 == 0), "pause/resume " + i); }
            ShowPage("recognition", false); UpdateLayout();
            RuleInput.Text = "smoke-editor.exe"; Click(AddRuleButton);
            Check(engine.Data.Settings.Rules.Any(r => r.Identifier == "smoke-editor.exe"), "rule added by UI");
            Click(AddRuleButton); Check(engine.Data.Settings.Rules.Count(r => r.Identifier == "smoke-editor.exe") == 1, "duplicate/empty input rejected");
            ShowPage("settings", false); UpdateLayout();
            GoalInput.Text = "NaN"; Click(SaveSettingsButton); Check(engine.Data.Settings.GoalSeconds == 14400, "invalid settings rejected");
            GoalInput.Text = "4"; IdleInput.Text = "30"; ResetInput.Minutes = 0; Click(SaveSettingsButton);
            var now = DateTimeOffset.Now;
            engine.ResetObservation(); engine.Observe(now, new("smoke-editor.exe", "Smoke Editor", 0));
            engine.Observe(now.AddSeconds(2), new("smoke-editor.exe", "Smoke Editor", 2));
            Check(engine.TodaySeconds(now) >= 2, "tracking reaches displayed daily total");
            engine.Observe(now.AddSeconds(3), new("smoke-editor.exe", "Smoke Editor", 30)); Check(engine.Current.State == "away", "idle boundary stops");
            engine.Observe(now.AddSeconds(4), new("smoke-editor.exe", "Smoke Editor", 0)); Check(engine.Current.State == "tracking", "activity resumes");
            engine.ResetObservation(); Persist(); Render();
            PageScroller.ScrollToTop(); SaveImage(Path.Combine(output, "settings.png"));
            BackToNavigation(null, new RoutedEventArgs()); await Task.Delay(300);
            Click(ExpandButton); await Task.Delay(300);
            // Screenshots of actual Windows rendering; no Mac raster exists in the handoff.
            engine.ResetObservation(); engine.Observe(DateTimeOffset.Now, new("smoke-editor.exe", "Smoke", 0)); Render();
            Check(GoalProgress.ScanAnimating, "2.8-second compositor scan starts while tracking");
            SaveImage(Path.Combine(output, "tracking-matrix.png"));
            await Task.Delay(400); SaveImage(Path.Combine(output, "tracking-matrix-motion.png"));
            engine.Data.Settings.ProgressStyle = "waveform"; Render(); SaveImage(Path.Combine(output, "tracking-waveform.png"));
            engine.Data.Settings.ProgressStyle = "matrix";
            engine.Observe(DateTimeOffset.Now, new("smoke-editor.exe", "Smoke", 30)); Render();
            Check(!GoalProgress.PulseAnimating, "unfinished away cells remain static during first 10 seconds");
            awaySince=DateTimeOffset.Now.AddSeconds(-11); Render();
            Check(GoalProgress.PulseAnimating, "2.8-second unfinished-cell breathing starts while away");
            await Task.Delay(400); SaveImage(Path.Combine(output, "away-matrix.png"));
            engine.Data.Settings.Paused = true; engine.Observe(DateTimeOffset.Now, new("smoke-editor.exe", "Smoke", 0)); Render();
            Check(!GoalProgress.ScanAnimating && !GoalProgress.PulseAnimating, "manual pause stops progress animations");
            await Task.Delay(400); SaveImage(Path.Combine(output, "paused.png"));
            engine.Data.Settings.Paused = false;
            engine.Data.Settings.ReduceMotion = true; visualState = ""; engine.ResetObservation(); Render();
            Check(!GoalProgress.ScanAnimating && !GoalProgress.PulseAnimating, "reduce motion disables continuous animations");
            engine.Data.Settings.ReduceMotion = false; visualState = ""; Render();
            foreach (var scale in new[] { 0.5, 0.65, 0.82, 1.0 })
            {
                engine.Data.Settings.IslandScale = scale; ApplyScale(); UpdateLayout(); ClampToScreen(); Render();
                Check(TimeLabel.ActualWidth > 0 && GoalProgress.ActualHeight > 0, "source layout scale " + scale);
            }
            engine.Data.Settings.IslandScale = 0.82; ApplyScale(); UpdateLayout(); ClampToScreen(); Render();
            HideIsland(null,new RoutedEventArgs()); Check(!IsVisible, "hide keeps process/tray alive"); ShowIsland(); Check(IsVisible, "show from tray handler");
            Check(!DetailsPanel.IsVisible, "navigation returned to timer");
            if (nativeSmoke) await RunNativeSmoke(checks);
            engine.Data.Settings.Paused = true;
            engine.Observe(DateTimeOffset.Now, WindowsActivity.Snapshot()); Persist(); Render();
            var snapshot = WindowsActivity.Snapshot();
            var report = new { passed = true, checks, foreground = snapshot.FrontmostApplicationIdentifier,
                idleSeconds = RuntimeCompat.IsFinite(snapshot.IdleSeconds) ? snapshot.IdleSeconds : -1,
                dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip, monitors = Forms.Screen.AllScreens.Length,
                persistedSeconds = engine.Data.Sessions.Sum(s => s.EffectiveSeconds), dataFile = store.Path,
                nativeSmoke,
                uiBaseline = new { baseWidth = 344, baseHeight = 92, defaultScale = 0.82, cornerRadius = 26, orb = 34, columns = 30, rows = 3, navigationSeconds = 0.24, statusSeconds = 0.35, scanSeconds = 2.8 },
                limitation = "UI uses button routed events. Native smoke, when enabled, sends F24 only to this test window and uses real Win32 foreground/idle readings. OS mouse clicking, real DPI changes and extra physical monitors were not tested." };
            File.WriteAllText(Path.Combine(output, "ui-smoke.json"), JsonSerializer.Serialize(report, AtomicStore.JsonOptions));
            await Task.Delay(250); ExitIsland(null, new RoutedEventArgs());
        }
        catch (Exception error) { SmokeFailed = true; File.WriteAllText(Path.Combine(output, "ui-smoke-error.txt"), error.ToString()); exiting = true; Close(); }
    }
    private async Task RunNativeSmoke(List<string> checks)
    {
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        var originalRules = engine.Data.Settings.Rules;
        engine.Data.Settings.Rules = [new("WorkIsland.exe", "Native adapter test")];
        engine.Data.Settings.Paused = false;
        ShowIsland();
        await Task.Delay(300);
        NativeSmokeInput.SendF24ToOwnWindow(new WindowInteropHelper(this).Handle);
        engine.ResetObservation();
        var input = WindowsActivity.Snapshot();
        Check(input.FrontmostApplicationIdentifier == "WorkIsland.exe", "native foreground identifies actual test process");
        Check(input.IdleSeconds < 2, "native input resets Win32 idle clock");
        var baseline = engine.Data.Sessions.Sum(s => s.EffectiveSeconds);
        var samples = new List<object>();
        ObserveActivity(input); Check(engine.Current.State == "tracking", "native work rule starts tracking");
        for (var i = 0; i < 36; i++)
        {
            await Task.Delay(1000);
            input = WindowsActivity.Snapshot(); ObserveActivity(input); Persist(); Render();
            samples.Add(new { second = i + 1, idleSeconds = RuntimeCompat.IsFinite(input.IdleSeconds) ? input.IdleSeconds : -1,
                foreground = input.FrontmostApplicationIdentifier, state = engine.Current.State });
            if (input.IdleSeconds >= 30) break;
        }
        var diagnosticDir = smokeOutput ?? Path.Combine(Path.GetDirectoryName(store.Path)!, "smoke");
        File.WriteAllText(Path.Combine(diagnosticDir, "native-samples.json"), JsonSerializer.Serialize(samples, AtomicStore.JsonOptions));
        Check(input.IdleSeconds >= 30 && engine.Current.State == "away", $"real 30-second idle threshold stops tracking (last idle={input.IdleSeconds:0.000}, state={engine.Current.State})");
        var earned = engine.Data.Sessions.Sum(s => s.EffectiveSeconds) - baseline;
        Check(earned >= 29 && earned <= 31, "native effective seconds bounded at idle threshold");
        NativeSmokeInput.SendF24ToOwnWindow(new WindowInteropHelper(this).Handle);
        await Task.Delay(200); ObserveActivity(WindowsActivity.Snapshot());
        Check(engine.Current.State == "tracking", "real input automatically resumes tracking");
        engine.Data.Settings.Paused = true; ObserveActivity(WindowsActivity.Snapshot());
        NativeSmokeInput.SendF24ToOwnWindow(new WindowInteropHelper(this).Handle);
        ObserveActivity(WindowsActivity.Snapshot()); Check(engine.Current.State == "paused", "real input cannot override manual pause");
        engine.Data.Settings.Paused = false; engine.ResetObservation(); engine.Data.Settings.Rules = originalRules; Persist(); Render();
    }
    private async Task RunRestartSmoke()
    {
        timer.Stop();
        var output = smokeOutput ?? Path.Combine(Path.GetDirectoryName(store.Path)!, "restart");
        Directory.CreateDirectory(output);
        try
        {
            var baseline = engine.Data.Sessions.Sum(s => s.EffectiveSeconds);
            if (baseline <= 0 || !engine.Data.Settings.Paused || engine.Data.Settings.GoalSeconds != 14400
                || !engine.Data.Settings.Rules.Any(r => r.Identifier == "smoke-editor.exe"))
                throw new InvalidOperationException("Previous process's work, goal, rule or paused state was not restored.");
            engine.Observe(DateTimeOffset.Now.AddHours(1), WindowsActivity.Snapshot());
            if (engine.Data.Sessions.Sum(s => s.EffectiveSeconds) != baseline || engine.Current.State != "paused")
                throw new InvalidOperationException("Restart credited offline time or lost manual pause.");
            File.WriteAllText(Path.Combine(output, "restart-smoke.json"), JsonSerializer.Serialize(new { passed = true, loadedSeconds = baseline,
                afterOfflineSimulationSeconds = engine.Data.Sessions.Sum(s => s.EffectiveSeconds), paused = engine.Data.Settings.Paused,
                rules = engine.Data.Settings.Rules.Count, goalSeconds = engine.Data.Settings.GoalSeconds,
                realWindowCreated = new WindowInteropHelper(this).Handle != IntPtr.Zero, trayRegistered = tray.Visible }, AtomicStore.JsonOptions));
            await Task.Delay(200); ExitIsland(null, new RoutedEventArgs());
        }
        catch (Exception error) { SmokeFailed = true; File.WriteAllText(Path.Combine(output, "restart-smoke-error.txt"), error.ToString()); exiting = true; Close(); }
    }
    private void SaveImage(string file)
    {
        UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth), (int)Math.Ceiling(ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file); encoder.Save(stream);
    }
}
