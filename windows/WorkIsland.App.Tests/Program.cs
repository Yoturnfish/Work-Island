using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    [STAThread] private static int Main(string[] args)
    {
        if(args.Length==2&&args[0]=="--startup-command-vectors")return WriteStartupCommandVectors(args[1]);
        if(args.Length==2 && args[0]=="--browser-sampler-only")return RunBrowserSamplerChecks(args[1]);
        if(args.Contains("--native-group-probe")){Console.Error.WriteLine("Retired XAML image-surface experiment. Use --native-drawing-group-probe.");return 2;}
        if (args.Length == 2 && args[0] == "--native-scan-probe") return RunNativeScanProbe(args[1]);
        if (args.Length == 2 && args[0] == "--native-layered-probe") return RunNativeScanProbe(args[1],true);
        if (args.Length == 2 && args[0] == "--fullscreen-native-probe") return RunFullscreenNativeProbe(args[1]);
        if (args.Length == 2 && args[0] == "--owned-modal-probe") return RunOwnedModalProbe(args[1]);
        if (args.Length == 2 && args[0] == "--browser-live-probe") return RunBrowserLiveProbe(args[1]);
        if(args.Length==2 && args[0]=="--fullscreen-state-readonly")
        {
            FullscreenReadOnly.Write(args[1]);return 0;
        }
        var output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        var results = new List<object>();
        var focusEvents = new List<object>();
        var failures = 0;
        void Check(string name, bool condition, object? evidence = null)
        {
            results.Add(new { name, passed = condition, evidence });
            if (!condition) failures++;
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Dispatcher.InvokeAsync(async () =>
        {
            IslandWindow? window = null;
            try
            {
                var state = new AppState { Settings = new() { Rules = [], Topmost = false } };
                window = new IslandWindow(new AtomicStore(Path.Combine(output, "isolated-state.json")), state, false, null,
                    startupRegistration:args.Contains("--startup-failure-live")?new StartupFailureFixture():new IsolatedStartupRegistration(Path.Combine(output,"startup-fixture.json"),"isolated-regression"),recognitionSource:new RecognitionFixture(),applicationPicker:new ApplicationPickerFixture(),fullscreenSource:new FullscreenFixture());
                var engine = (TrackingEngine)typeof(IslandWindow).GetField("engine", Flags)!.GetValue(window)!;
                var timer = (DispatcherTimer)typeof(IslandWindow).GetField("timer", Flags)!.GetValue(window)!;
                Check("ordinary startup restores Mac default topmost even from an older false setting", window.Topmost && state.Settings.Topmost);
                if(args.Contains("--native-group-input-review")||args.Contains("--native-controller-input")||args.Contains("--native-pulse-controller-input")||args.Contains("--mode-native-input")||args.Contains("--key-release-live")||args.Contains("--reminder-live")||args.Contains("--preference-save-live")||args.Contains("--startup-failure-live")||args.Contains("--pause-reset-live")||args.Contains("--profile-save-live"))window.ShowInTaskbar=true;
                if(args.Contains("--profile-style-focus-live"))window.ShowInTaskbar=true;
                if(args.Contains("--software-wpf"))window.SourceInitialized+=(_,_)=>((System.Windows.Interop.HwndSource)PresentationSource.FromVisual(window)!).CompositionTarget.RenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
                window.ShowActivated = false;
                window.Topmost = false;
                window.Loaded += (_, _) => timer.Stop();
                window.Show();
                await Task.Delay(100);
                if(args.Contains("--pointer-input-compat")&&!ObservePointerBackend(output,Check))return;
                if(args.Contains("--pointer-popup-exit"))
                {
                    if(!args.Contains("--pointer-input-compat"))throw new InvalidOperationException("Pointer popup exit requires verified candidate backend; Wisp negative is not rerun.");
                    await RunPointerProductPopupExit(window,output,Check);return;
                }
                if(args.Skip(1).Contains("--navigation-smoothness")){await RunNavigationSmoothness(window,output,Check);return;}
                if(args.Skip(1).Contains("--live-foreground-cost")){await RunLiveForegroundCost(window,output,Check);return;}
                if(args.Skip(1).Contains("--navigation-smoothness-live")){await RunNavigationSmoothness(window,output,Check,true);return;}
                if(args.Skip(1).Contains("--release-handoff")){await RunReleaseHandoff(window,output,Check);return;}
                if(args.Skip(1).Contains("--release-handoff-live")){await RunReleaseHandoff(window,output,Check,true);return;}
                if(args.Skip(1).Contains("--native-progress-only")){await RunNativeProgressChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--native-progress-resource")){await RunNativeProgressResource(window,output);return;}
                if(args.Skip(1).Contains("--native-drawing-group-probe")){await RunNativeGroupProbe(window,output,Check);return;}
                if(args.Skip(1).Contains("--native-group-live")){await RunNativeGroupLive(window,output,Check);return;}
                if(args.Skip(1).Contains("--native-group-input-review")){await RunNativeGroupLive(window,output,Check,true);return;}
                if(args.Skip(1).Contains("--native-disposal-only")){await RunNativeDisposalChecks(window,Check);return;}
                if(args.Skip(1).Contains("--native-surface-lifecycle")){await RunNativeSurfaceLifecycle(window,output,Check);return;}
                if(args.Skip(1).Contains("--native-phase-handoff")){await RunNativePhaseHandoff(window,output,Check);return;}
                if(args.Skip(1).Contains("--native-controller")){await RunNativeForegroundController(window,output,Check,args.Contains("--perf-only"));return;}
                if(args.Skip(1).Contains("--native-quiet-audit")){await RunNativeQuietAudit(window,output);return;}
                if(args.Skip(1).Contains("--state-resource-audit")){await RunStateResourceAudit(window,output);return;}
                if(args.Skip(1).Contains("--state-resource-controls")){await RunStateResourceControls(window,output);return;}
                if(args.Skip(1).Contains("--identity-benchmark")){RunIdentityBenchmark(window,output,Check);return;}
                if(args.Skip(1).Contains("--pulse-curve-review")){await RunPulseCurveReview(window,output,Check);return;}
                if(args.Skip(1).Contains("--native-pulse-probe")){await RunNativePulseProbe(window,output,Check,args.Contains("--pixels-only"));return;}
                if(args.Skip(1).Contains("--native-raster-fresh")){window.Hide();await RunNativeRasterFresh(output,Check,args.Contains("--software"),args.Contains("--product"));return;}
                if(args.Skip(1).Contains("--native-raster-review")||args.Skip(1).Contains("--native-raster-backend")){await RunNativeRasterReview(window,output,Check,args.Contains("--native-raster-backend"));return;}
                if(args.Skip(1).Contains("--native-edge-review")||args.Skip(1).Contains("--native-edge-repair")){await RunNativeEdgeReview(window,output,Check,args.Contains("--native-edge-repair"),args.Contains("--glass"));return;}
                if(args.Skip(1).Contains("--native-pulse-controller")){await RunNativePulseController(window,output,Check);return;}
                if(args.Skip(1).Contains("--native-pulse-controller-pixels")){await RunNativePulseControllerPixels(window,output,Check,args.Contains("--glass"));return;}
                if(args.Skip(1).Contains("--native-pulse-controller-resource")){await RunNativePulseControllerResource(window,output,Check);return;}
                if(args.Skip(1).Contains("--native-controller-input")||args.Skip(1).Contains("--native-pulse-controller-input")){await RunNativeControllerInputReview(window,output,Check,args.Contains("--native-pulse-controller-input"));return;}
                if(args.Skip(1).Contains("--picker-focus")){await RunPickerFocusChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--input-origin")){RunInputOriginChecks(Check);return;}
                if(args.Skip(1).Contains("--pointer-snapshot-live")){await RunPointerSnapshotLive(window,output,Check);return;}
                if(args.Skip(1).Contains("--input-origin-live")){await RunInputOriginLive(window,output,Check);return;}
                if(args.Skip(1).Contains("--fullscreen-native-focused")){await RunFullscreenNativeChecks(output,Check);return;}
                if(args.Skip(1).Contains("--fullscreen-overlay-focused")){RunFullscreenOverlayPolicy(Check);await RunFullscreenChecks(window,Check);await RunFullscreenNativeChecks(output,Check);return;}
                if(args.Skip(1).Contains("--startup-provider-related")){await RunStartupFailureChecks(output,Check);await RunInstanceControlChecks(output,Check);FeatureRegressions.RunRegistryOnly(output,Check);return;}
                if(args.Contains("--presentation-path")){await RunPresentationPathChecks(window,output,Check);return;}
                if(args.Contains("--status-clock")){RunStatusClockArtworkChecks(output,Check);return;}
                if(args.Contains("--status-clock-live")){await RunStatusClockLive(window,output,Check);return;}
                if(args.Contains("--status-clock-related")){await RunMenuBarChecks(window,Check);await RunStatusMotionChecks(window,Check);await RunStatusRecoveryChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--website-preview")||args.Skip(1).Contains("--website-preview-live")){await RunWebsitePreviewChecks(window,output,Check,args.Contains("--website-preview-live"));return;}
                if(args.Skip(1).Contains("--website-preview-related")){await RunRecognitionChecks(window,Check);await RunRecognitionSaveChecks(window,output,Check);await RunRecognitionMarksChecks(window,output,Check,false);await RunRecognitionReopenChecks(window,output,Check);await RunWebsiteAddressChecks(window,output,Check,false);await RunWebsiteKeywordChecks(window,output,Check,false);await RunRecognitionFailureChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--website-keyword")||args.Skip(1).Contains("--website-keyword-live")){await RunWebsiteKeywordChecks(window,output,Check,args.Contains("--website-keyword-live"));return;}
                if(args.Skip(1).Contains("--website-keyword-related")){await RunRecognitionChecks(window,Check);await RunRecognitionSaveChecks(window,output,Check);await RunRecognitionMarksChecks(window,output,Check,false);await RunRecognitionReopenChecks(window,output,Check);await RunWebsiteAddressChecks(window,output,Check,false);await RunRecognitionFailureChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--website-address")||args.Skip(1).Contains("--website-address-live")){await RunWebsiteAddressChecks(window,output,Check,args.Contains("--website-address-live"));return;}
                if(args.Skip(1).Contains("--website-address-related")){await RunRecognitionChecks(window,Check);await RunRecognitionSaveChecks(window,output,Check);await RunRecognitionMarksChecks(window,output,Check,false);await RunRecognitionReopenChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--recognition-opening")){await RunRecognitionOpeningChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--recognition-marks")||args.Skip(1).Contains("--recognition-marks-live")){await RunRecognitionMarksChecks(window,output,Check,args.Contains("--recognition-marks-live"));return;}
                if(args.Skip(1).Contains("--recognition-save")){await RunRecognitionSaveChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--recognition-reopen")){await RunRecognitionReopenChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--recognition-failure")){await RunRecognitionFailureChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--day-reset")){await RunDayResetChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--key-release-live")){await RunKeyReleaseLive(window,output,Check);return;}
                if(args.Skip(1).Contains("--key-release")){await RunKeyReleaseChecks(window,Check);return;}
                if(args.Skip(1).Contains("--legacy-snapshot-ui")){await RunLegacySnapshotUi(window,output,Check);return;}
                if(args.Skip(1).Contains("--reminder-resource")){await RunReminderResource(window,output,Check);return;}
                if(args.Skip(1).Contains("--reminder-geometry")){await RunReminderGeometryChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--reminder-geometry-related")){await RunReminderScheduleChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--reminder-schedule")){await RunReminderScheduleChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--reminder-live")){await RunReminderScheduleLive(window,output,Check);return;}
                if(args.Skip(1).Contains("--settings-numeric")){await RunNumericPreferenceChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--preference-save")){await RunPreferenceSaveChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--preference-save-live")){await RunPreferenceSaveLive(window,output,Check);return;}
                if(args.Skip(1).Contains("--startup-failure")){await RunStartupFailureChecks(output,Check);return;}
                if(args.Skip(1).Contains("--startup-failure-live")){await RunStartupFailureLive(window,output,Check);return;}
                if(args.Skip(1).Contains("--pause-reset")){await RunPauseResetChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--pause-reset-live")){await RunPauseResetLive(window,output,Check);return;}
                if(args.Skip(1).Contains("--profile-save")){await RunProfileSaveChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--profile-save-live")){await RunProfileSaveLive(window,output,Check);return;}
                if(args.Skip(1).Contains("--profile-labels-review")){window.ShowInTaskbar=true;await RunProfileLabelsChecks(window,output,Check,true);return;}
                if(args.Skip(1).Contains("--profile-labels")){await RunProfileLabelsChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--profile-editor-layout")){await RunProfileEditorLayout(window,output,Check);return;}
                if(args.Skip(1).Contains("--profile-editor-layout-live")){await RunProfileEditorLayout(window,output,Check,true);return;}
                if(args.Skip(1).Contains("--save-feedback")){await RunSaveFeedbackChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--tray-fullscreen")){await RunTrayFullscreenChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--tray-fullscreen-live")){await RunTrayFullscreenLive(window,output,Check);return;}
                if(args.Skip(1).Contains("--runtime-cost-audit")){await RunRuntimeCostAudit(window,output,Check);return;}
                if(args.Skip(1).Contains("--contrast-state-timing")){await RunContrastStateTiming(window,output,Check);return;}
                if(args.Skip(1).Contains("--adaptive-text")||args.Skip(1).Contains("--adaptive-text-review")){await RunAdaptiveTextChecks(window,output,Check,args.Contains("--adaptive-text-review"));return;}
                if(args.Skip(1).Contains("--warm-native-glass-first")){await RunNativeWarmAudit(window,output,Check,false);return;}
                if(args.Skip(1).Contains("--warm-native-black-first")){await RunNativeWarmAudit(window,output,Check,true);return;}
                if(args.Skip(1).Contains("--status-recovery")){await RunStatusRecoveryChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--mode-fullscreen-boundary")){await RunModeFullscreenBoundaryChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--mode-save")){await RunModeSaveChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--mode-save-related")){await RunMenuBarChecks(window,Check);await RunStatusMotionChecks(window,Check);await RunSettingsEffectChecks(window,Check);return;}
                if(args.Skip(1).Contains("--mode-native-input")){await RunNativeModeInput(window,output,Check);return;}
                if(args.Skip(1).Contains("--native-drag-review")){await RunNativeDragReview(window,output,Check);return;}
                if(args.Skip(1).Contains("--complex-glass-motion")){await RunComplexGlassMotion(window,output,Check);return;}
                if(args.Skip(1).Contains("--visible-render-audit")){await RunVisibleRenderingAudit(window,output);return;}
                if(args.Skip(1).Contains("--visible-thread-audit")){await RunVisibleThreadAudit(window,output);return;}
                if(args.Skip(1).Contains("--compact-lifecycle-only")){await RunCompactLifecycleChecks(window,Check);return;}
                if(args.Skip(1).Contains("--compact-related"))
                {
                    await RunCompactLifecycleChecks(window,Check);
                    await RunTransitionChecks(window,Check);
                    await RunBehaviorMotionChecks(window,Check);
                    await RunMenuBarChecks(window,Check);
                    await RunStatusMotionChecks(window,Check);
                    await RunOnboardingChecks(output,Check);
                    await RunMaterialChecks(output,Check);return;
                }
                if(args.Skip(1).Contains("--compact-resource-audit")){await RunCompactResourceAudit(window,output,!args.Contains("--no-transform-observers"));return;}
                if(args.Skip(1).Contains("--history-save-visual-only")){await RunHistorySaveVisualChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--click-handoff")){await RunClickHandoff(window,output,Check);return;}
                if(args.Skip(1).Contains("--history-focus-review")){await RunHistoryFocusReview(window,output,Check);return;}
                if(args.Skip(1).Contains("--profile-preview-motion")){await RunProfilePreviewMotion(window,output,Check);return;}
                if(args.Skip(1).Contains("--profile-lifetime")){await RunProfileLifetime(window,output,Check);return;}
                if(args.Skip(1).Contains("--profile-style-focus")||args.Skip(1).Contains("--profile-style-focus-live")){await RunProfileStyleFocus(window,output,Check,args.Contains("--profile-style-focus-live"));return;}
                if(args.Skip(1).Contains("--profile-name-only")){await RunProfileNameOnly(window,output,Check);return;}
                if(args.Skip(1).Contains("--profile-context-refresh-live")){await RunProfileContextRefreshLive(window,output,Check);return;}
                if(args.Skip(1).Contains("--profile-context-refresh")){await RunProfileContextRefresh(window,output,Check);return;}
                if(args.Skip(1).Contains("--profile-opening-live")){await RunProfileOpeningLive(window,output,Check);return;}
                if(args.Skip(1).Contains("--profile-opening")){await RunProfileOpeningChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--aggregate-duration")){await RunAggregateDurationChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--session-structure")){await RunSessionStructureChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--history-fields")){await RunHistoryFieldChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--history-announcement")||args.Skip(1).Contains("--history-announcement-live")){await RunHistoryAnnouncementChecks(window,output,Check,args.Contains("--history-announcement-live"));return;}
                if(args.Skip(1).Contains("--history-edit-only")){await RunHistoryEditChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--history-related"))
                {
                    await RunHistoryEditChecks(window,output,Check);
                    await RunHistoryPerformanceChecks(window,Check);
                    await RunSettingsEffectChecks(window,Check);
                    await RunBehaviorMotionChecks(window,Check);
                    await RunExitSafetyChecks(output,Check);
                    await RunLocalizationChecks(window,output,Check);return;
                }
                if(args.Skip(1).Contains("--localization-only")){await RunLocalizationChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--localization-related"))
                {
                    await RunLocalizationChecks(window,output,Check);
                    await RunSettingsEffectChecks(window,Check);
                    await RunRecognitionChecks(window,Check);
                    FeatureRegressions.Run(window,state,engine,output,Check);
                    await RunOwnedModalChecks(output,Check);return;
                }
                if(args.Skip(1).Contains("--opacity-pixels-only")){await RunOpacityPixelChecks(output,Check);return;}
                if(args.Skip(1).Contains("--system-material-policy")){await RunSystemMaterialPolicyChecks(window,output,Check);return;}
                if(args.Skip(1).Contains("--material-only")){await RunMaterialChecks(output,Check);return;}
                if(args.Skip(1).Contains("--owned-modal-only")){await RunOwnedModalChecks(output,Check);return;}
                if(args.Skip(1).Contains("--onboarding-only")){await RunOnboardingChecks(output,Check);return;}
                if(args.Skip(1).Contains("--instance-control-only")){await RunInstanceControlChecks(output,Check);return;}
                if(args.Skip(1).Contains("--exit-safety-only")){await RunExitSafetyChecks(output,Check);return;}
                await RunExitSafetyChecks(output,Check);
                await RunInstanceControlChecks(output,Check);
                await RunOnboardingChecks(output,Check);
                await RunOwnedModalChecks(output,Check);
                await RunMaterialChecks(output,Check);
                await RunCompactLifecycleChecks(window,Check);
                await RunOpacityPixelChecks(output,Check);
                await RunLocalizationChecks(window,output,Check);
                await RunHistoryEditChecks(window,output,Check);
                if(args.Skip(1).Contains("--history-performance-only")){await RunHistoryPerformanceChecks(window,Check);return;}
                if(args.Skip(1).Contains("--status-motion-only")){await RunStatusMotionChecks(window,Check);return;}
                await RunMenuBarChecks(window, Check);
                await RunSettingsEffectChecks(window, Check);
                if (args.Skip(1).Contains("--menu-bar-only")) return;
                await RunHistoryPerformanceChecks(window,Check);
                await RunStatusMotionChecks(window,Check);
                await RunFullscreenChecks(window, Check);
                await RunFullscreenNativeChecks(output, Check);
                if (args.Skip(1).Contains("--fullscreen-only")) return;
                await RunBehaviorMotionChecks(window, Check);
                if (args.Skip(1).Contains("--behavior-motion-only")) return;
                await RunRecognitionChecks(window, Check);
                if (args.Skip(1).Contains("--recognition-only")) return;
                await RunTransitionChecks(window, Check);
                if (args.Skip(1).Contains("--transitions-only")) return;
                typeof(IslandWindow).GetMethod("ToggleExpanded",Flags)!.Invoke(window,[window,new RoutedEventArgs()]);
                await Task.Delay(280);
                typeof(IslandWindow).GetMethod("OpenSettings",Flags)!.Invoke(window,[window,new RoutedEventArgs()]);
                await Task.Delay(300);
                var settingsPage=(StackPanel)typeof(IslandWindow).GetField("SettingsPage",Flags)!.GetValue(window)!;
                var scroller=(ScrollViewer)typeof(IslandWindow).GetField("PageScroller",Flags)!.GetValue(window)!;
                string[] VisibleSettings() => settingsPage.Children.OfType<FrameworkElement>().Where(row=>row.Visibility==Visibility.Visible)
                    .Select(row=>row is Button ? "退出 Work Island" : row is Grid grid
                        ? grid.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? "登录后启动" : "unexpected").ToArray();
                var expectedSettings=new[]{"登录后启动","显示模式","背景样式","透明度","时间格式","每日目标","进入游离状态","游离计时","超级提醒","每日刷新","语言","退出 Work Island"};
                Check("visible settings follow Mac order without extra Windows rows",VisibleSettings().SequenceEqual(expectedSettings),VisibleSettings());
                IEnumerable<DependencyObject> Descendants(DependencyObject root)
                {
                    yield return root;
                    for(var child=0;child<VisualTreeHelper.GetChildrenCount(root);child++)
                        foreach(var item in Descendants(VisualTreeHelper.GetChild(root,child)))yield return item;
                }
                Check("buttons and page surfaces do not expose hover tooltips",Descendants(window).OfType<FrameworkElement>().All(element=>element.ToolTip is null));
                var scrollBar=(MacVerticalScrollBar)scroller.Template.FindName("PART_VerticalScrollBar",scroller);
                var scrollThumb=(System.Windows.Controls.Primitives.Thumb)scrollBar.Template.FindName("MacScrollKnob",scrollBar);
                foreach(var scaleFactor in new[]{.5,.82,1d})
                {
                    state.Settings.IslandScale=scaleFactor;
                    typeof(IslandWindow).GetMethod("ApplyScale",Flags)!.Invoke(window,null);
                    window.UpdateLayout();await Task.Delay(50);
                    foreach(var fraction in new[]{0d,.5,1d})
                    {
                        scroller.ScrollToVerticalOffset(scroller.ScrollableHeight*fraction);
                        window.UpdateLayout();await Task.Delay(50);window.UpdateLayout();
                        var position=scrollThumb.TranslatePoint(new Point(),scrollBar);
                        Check($"entire scrollbar knob fits track at scale {scaleFactor} / offset {fraction}",
                            scrollThumb.ActualHeight>0 && position.Y>=-.01 && position.Y+scrollThumb.ActualHeight<=scrollBar.ActualHeight+.01,
                            new{position.Y,knob=scrollThumb.ActualHeight,track=scrollBar.ActualHeight,scroller.VerticalOffset});
                    }
                }
                state.Settings.IslandScale=.82;typeof(IslandWindow).GetMethod("ApplyScale",Flags)!.Invoke(window,null);
                scroller.ScrollToTop();await Task.Delay(60);window.UpdateLayout();
                scrollThumb.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0,0){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragStartedEvent});
                scrollThumb.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(0,8){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragDeltaEvent});
                await Task.Delay(60);window.UpdateLayout();
                Check("scrollbar drag changes actual content offset and keeps knob inside track",scroller.VerticalOffset>0 && scrollBar.KnobOffset+scrollThumb.ActualHeight<=scrollBar.ActualHeight+.01);
                scroller.ScrollToBottom();await Task.Delay(60);window.UpdateLayout();
                var resetLabel=settingsPage.Children.OfType<Grid>().SelectMany(row=>row.Children.OfType<TextBlock>()).Single(label=>label.Text=="每日刷新");
                var labelPosition=resetLabel.TranslatePoint(new Point(),scroller);
                var resetControl=(MacDayResetInput)typeof(IslandWindow).GetField("ResetInput",Flags)!.GetValue(window)!;
                var controlPosition=resetControl.TranslatePoint(new Point(),scroller);
                Check("Mac bottom view displays complete daily reset label and two-field control",labelPosition.Y>=0 && labelPosition.Y+resetLabel.ActualHeight<=scroller.ActualHeight && controlPosition.Y>=0 && controlPosition.Y+resetControl.ActualHeight<=scroller.ActualHeight,new{labelPosition.Y,labelHeight=resetLabel.ActualHeight,controlY=controlPosition.Y,controlHeight=resetControl.ActualHeight,viewport=scroller.ActualHeight});
                var macBackgroundPicker=(ComboBox)typeof(IslandWindow).GetField("BackgroundPicker",Flags)!.GetValue(window)!;
                macBackgroundPicker.SelectedItem=macBackgroundPicker.Items.Cast<ComboBoxItem>().First(item=>item.Tag?.ToString()=="glass");
                window.UpdateLayout();
                Check("transparent settings omit conditional opacity row and retain Mac final rows",VisibleSettings().SequenceEqual(expectedSettings.Where(label=>label!="透明度")),VisibleSettings());
                macBackgroundPicker.SelectedItem=macBackgroundPicker.Items.Cast<ComboBoxItem>().First(item=>item.Tag?.ToString()=="black");
                var resetInput=(MacDayResetInput)typeof(IslandWindow).GetField("ResetInput",Flags)!.GetValue(window)!;
                resetInput.Minutes=0;
                Check("Mac midnight display keeps fixed colon and shows 24 / 00",resetInput.HourField.Text=="24" && resetInput.MinuteField.Text=="00");
                var resetEvents=0;resetInput.MinutesChanged+=(_,_)=>resetEvents++;
                resetInput.Minutes=305;
                resetInput.HourField.Text="";resetInput.MinuteField.Text="";
                resetInput.HourField.Text="06";resetInput.MinuteField.Text="07";
                Check("two complete reset fields immediately commit to actual settings",resetInput.Minutes==367 && state.Settings.DayResetMinutes==367 && resetEvents==1);
                resetInput.FinishEditing();resetInput.FinishEditing();
                Check("unchanged reset synchronization does not emit or migrate again",resetEvents==1);
                resetInput.HourField.Text="24";resetInput.MinuteField.Text="01";resetInput.FinishEditing();
                Check("invalid 24:01 keeps previous committed reset",resetInput.Minutes==367 && resetInput.HourField.Text=="06" && resetInput.MinuteField.Text=="07");
                resetInput.HourField.Text="";resetInput.MinuteField.Text="8";resetInput.FinishEditing();
                Check("partially cleared reset keeps previous committed reset",resetInput.Minutes==367 && resetInput.MinuteField.Text=="07");
                resetInput.HourField.Text="";resetInput.MinuteField.Text="";resetInput.FinishEditing();
                Check("entirely cleared reset commits midnight and restores 24:00",resetInput.Minutes==0 && state.Settings.DayResetMinutes==0 && resetInput.HourField.Text=="24" && resetInput.MinuteField.Text=="00");
                resetInput.HourField.Text="a123";
                Check("reset components strip non-digits and cap at two digits",resetInput.HourField.Text=="12");
                resetInput.HourField.Text="24";resetInput.MinuteField.Text="00";resetInput.FinishEditing();
                typeof(IslandWindow).GetMethod("CollapseToTimer",Flags)!.Invoke(window,null);await Task.Delay(280);
                bool Suspended() => (bool)typeof(IslandWindow).GetField("suspended", Flags)!.GetValue(window)!;
                async Task Session(SessionSwitchReason reason)
                {
                    typeof(IslandWindow).GetMethod("SessionSwitch", Flags)!.Invoke(window, [null, new SessionSwitchEventArgs(reason)]);
                    await Task.Delay(60);
                }
                async Task Power(PowerModes mode)
                {
                    typeof(IslandWindow).GetMethod("PowerModeChanged", Flags)!.Invoke(window, [null, new PowerModeChangedEventArgs(mode)]);
                    await Task.Delay(60);
                }
                await Session(SessionSwitchReason.SessionLock);
                Check("lock suspends and discards observation", Suspended() && engine.LastObservation is null);
                await Power(PowerModes.StatusChange);
                Check("power status notification cannot clear lock", Suspended() && engine.LastObservation is null);
                await Power(PowerModes.Suspend);
                await Power(PowerModes.Resume);
                Check("lock -> sleep -> wake remains locked", Suspended() && engine.LastObservation is null);
                await Session(SessionSwitchReason.SessionUnlock);
                Check("unlock after wake starts fresh observation", !Suspended() && engine.LastObservation is not null);
                await Power(PowerModes.Suspend);
                await Power(PowerModes.StatusChange);
                Check("power status notification cannot clear sleep", Suspended() && engine.LastObservation is null);
                await Session(SessionSwitchReason.SessionUnlock);
                Check("unlock while sleeping cannot resume", Suspended() && engine.LastObservation is null);
                await Power(PowerModes.Resume);
                Check("wake after unlock starts fresh observation", !Suspended() && engine.LastObservation is not null);
                await Power(PowerModes.StatusChange);
                Check("ordinary power notification preserves active observation", !Suspended() && engine.LastObservation is not null);
                Check("simulated notification sequences do not earn work", state.Sessions.Count == 0);
                FeatureRegressions.Run(window,state,engine,output,Check);
                typeof(IslandWindow).GetField("elapsedState",Flags)!.SetValue(window,"away");
                typeof(IslandWindow).GetField("awaySince",Flags)!.SetValue(window,DateTimeOffset.Now.AddHours(-1));
                typeof(IslandWindow).GetMethod("TimeChanged",Flags)!.Invoke(window,[null,EventArgs.Empty]);
                await Task.Delay(100);
                Check("time-change notification discards work observation and old away duration",engine.LastObservation is null
                    && typeof(IslandWindow).GetField("awaySince",Flags)!.GetValue(window) is null
                    && Equals(typeof(IslandWindow).GetField("elapsedState",Flags)!.GetValue(window),""));
                var originalLeft=window.Left;var originalTop=window.Top;
                window.Left=-100000;window.Top=-100000;
                typeof(IslandWindow).GetMethod("DisplaySettingsChanged",Flags)!.Invoke(window,[null,EventArgs.Empty]);
                await Task.Delay(250);
                var reviewHandle=new System.Windows.Interop.WindowInteropHelper(window).Handle;
                var workArea=System.Windows.Forms.Screen.FromHandle(reviewHandle).WorkingArea;
                var origin=window.PointToScreen(new Point(0,0));var screenDpi=VisualTreeHelper.GetDpi(window);
                var nativeRect=new { Left=origin.X,Top=origin.Y,Right=origin.X+window.ActualWidth*screenDpi.DpiScaleX,Bottom=origin.Y+window.ActualHeight*screenDpi.DpiScaleY };
                Check("display-change recovery brings offscreen island into current physical work area",
                    nativeRect.Left>=workArea.Left && nativeRect.Top>=workArea.Top && nativeRect.Right<=workArea.Right && nativeRect.Bottom<=workArea.Bottom,
                    new{nativeRect.Left,nativeRect.Top,nativeRect.Right,nativeRect.Bottom,workArea});
                var recovered=new AtomicStore(Path.Combine(output,"isolated-state.json")).Load();
                Check("display-change recovery persists the recovered position",recovered.Settings.WindowX==window.Left && recovered.Settings.WindowY==window.Top);
                window.Hide();
                typeof(IslandWindow).GetMethod("DisplaySettingsChanged",Flags)!.Invoke(window,[null,EventArgs.Empty]);
                await Task.Delay(150);
                Check("display-change recovery does not reveal a hidden island",!window.IsVisible);
                window.Left=originalLeft;window.Top=originalTop;window.Show();

                var icon = (Grid)typeof(IslandWindow).GetField("StatusIcon", Flags)!.GetValue(window)!;
                var scale = (ScaleTransform)icon.RenderTransform;
                void Render() => typeof(IslandWindow).GetMethod("Render", Flags, Type.EmptyTypes)!.Invoke(window, null);
                async Task<bool> Until(Func<bool> condition,int timeoutMs=4000)
                {
                    var watch=Stopwatch.StartNew();
                    while(!condition() && watch.ElapsedMilliseconds<timeoutMs)await Task.Delay(25);
                    return condition();
                }
                state.Settings.Paused=false;engine.ResetObservation();
                var backgroundPicker=(ComboBox)typeof(IslandWindow).GetField("BackgroundPicker",Flags)!.GetValue(window)!;
                var titleLabel=(TextBlock)typeof(IslandWindow).GetField("TitleLabel",Flags)!.GetValue(window)!;
                void Background(string mode)
                {
                    backgroundPicker.SelectedItem=backgroundPicker.Items.Cast<ComboBoxItem>().First(item=>item.Tag?.ToString()==mode);
                    Render();
                }
                Background("black");Check("black away title retains tracking text density",engine.Current.State=="away" && titleLabel.Opacity==1 && titleLabel.FontWeight==FontWeights.SemiBold);
                Background("glass");Check("switching away background to translucent restores title",titleLabel.Opacity==1);
                window.UpdateLayout();
                var translucent=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);translucent.Render(window);
                var pixels=new byte[4];translucent.CopyPixels(new Int32Rect((int)window.ActualWidth/2,8,1,1),pixels,4,0);
                var surfaces=typeof(IslandWindow).GetField("surfaces",Flags)!.GetValue(window)!;
                var acrylic=(bool)surfaces.GetType().GetProperty("NativeBackdropAvailable")!.GetValue(surfaces)!;
                Check("foreground alpha matches compositor or solid fallback",window.AllowsTransparency && (acrylic ? pixels[3]>0 && pixels[3]<100 : pixels[3]==255),new{sampleAlpha=pixels[3],acrylic});
                Check("background option uses original Mac Transparent label",((ComboBoxItem)backgroundPicker.SelectedItem).Content?.ToString()=="透明");
                Background("black");Check("switching back to black preserves away text density",titleLabel.Opacity==1 && titleLabel.FontWeight==FontWeights.SemiBold);
                state.Settings.Paused=true;engine.Observe(DateTimeOffset.Now,new("unmatched.exe","Unmatched",0));Render();
                Check("black paused title remains fully visible",titleLabel.Opacity==1);
                var retainedSessions=state.Sessions.ToArray();
                var today=DateOnly.ParseExact(LogicalDay.Key(DateTimeOffset.Now,engine.Zone,state.Settings.DayResetMinutes),"yyyy-MM-dd",CultureInfo.InvariantCulture);
                (DateOnly Date,double Seconds)[] HistoryDays(DateOnly latest)=>( (IEnumerable<(DateOnly Date,double Seconds)>)typeof(IslandWindow).GetMethod("CompleteHistoryDays",Flags)!.Invoke(window,[latest])!).ToArray();
                state.Sessions.Clear();Check("empty statistics have no invented dates",HistoryDays(today).Length==0);
                state.Sessions.Add(new Session{DayKey=today.AddDays(-45).ToString("yyyy-MM-dd"),EffectiveSeconds=120});
                state.Sessions.Add(new Session{DayKey=today.AddDays(-45).ToString("yyyy-MM-dd"),EffectiveSeconds=60});
                state.Sessions.Add(new Session{DayKey=today.AddDays(-2).ToString("yyyy-MM-dd"),EffectiveSeconds=30});
                state.Sessions.Add(new Session{DayKey=today.AddDays(1).ToString("yyyy-MM-dd"),EffectiveSeconds=90});
                var completeDays=HistoryDays(today);
                Check("statistics include oldest record beyond 28 days through today",completeDays.Length==46 && completeDays[0].Date==today.AddDays(-45) && completeDays[^1].Date==today);
                Check("statistics aggregate records and fill missing dates with zero",completeDays[0].Seconds==180 && completeDays[1].Seconds==0 && completeDays[^1].Seconds==0);
                Check("statistics exclude future records",completeDays.All(day=>day.Date<=today));
                Check("statistics respect caller logical-day endpoint",HistoryDays(today.AddDays(-1))[^1].Date==today.AddDays(-1));
                typeof(IslandWindow).GetMethod("ShowHistoryStatistics",Flags)!.Invoke(window,null);
                var historyRows=(StackPanel)typeof(IslandWindow).GetField("HistoryRows",Flags)!.GetValue(window)!;
                var historyStrip=(HistoryStatisticsStrip)((ScrollViewer)historyRows.Children[1]).Content;
                Check("statistics UI retains entire date range without realizing daily controls",historyStrip.DayCount==46 && historyStrip.FirstDay==today.AddDays(-45) && historyStrip.LastDay==today && historyStrip.SecondsOn(today.AddDays(-45))==180);
                typeof(IslandWindow).GetField("historyMode",Flags)!.SetValue(window,"list");typeof(IslandWindow).GetField("historySignature",Flags)!.SetValue(window,"");typeof(IslandWindow).GetMethod("RefreshHistory",Flags)!.Invoke(window,null);
                Check("recent history list remains seven days",historyRows.Children.Count==8);
                state.Sessions.RemoveAll(session=>session.DayKey!=today.AddDays(1).ToString("yyyy-MM-dd"));
                Check("future-only statistics are empty",HistoryDays(today).Length==0);
                state.Sessions.Clear();state.Sessions.AddRange(retainedSessions);
                // Establish a neutral scale before measuring the repeat period.
                // Transitions from an existing breath now include a smooth handoff.
                state.Settings.ReduceMotion=true;Render();state.Settings.ReduceMotion=false;
                state.Settings.Paused = true;
                engine.Observe(DateTimeOffset.Now, new("unmatched.exe", "Unmatched", 0));
                typeof(IslandWindow).GetField("visualState", Flags)!.SetValue(window, "");
                Render();
                await Task.Delay(900);
                Check("pause icon reaches minimum at one-way 0.9s", Math.Abs(scale.ScaleX - .86) < .012, new { seconds = .9, scale = scale.ScaleX });
                await Task.Delay(900);
                Check("pause icon returns at full 1.8s", Math.Abs(scale.ScaleX - 1) < .012, new { seconds = 1.8, scale = scale.ScaleX });
                for(int cycle=2;cycle<=3;cycle++)
                {
                    await Task.Delay(900);Check("pause cycle "+cycle+" minimum",Math.Abs(scale.ScaleX-.86)<.012,new{scale=scale.ScaleX});
                    await Task.Delay(900);Check("pause cycle "+cycle+" return",Math.Abs(scale.ScaleX-1)<.012,new{scale=scale.ScaleX});
                }
                state.Settings.Paused = false;
                engine.ResetObservation();
                Render();
                await Task.Delay(1400);
                Check("away icon is midway at 1.4s, not maximum", Math.Abs(scale.ScaleX - 1.04) < .014, new { seconds = 1.4, scale = scale.ScaleX });
                await Task.Delay(1400);
                Check("away icon reaches maximum at one-way 2.8s", Math.Abs(scale.ScaleX - 1.08) < .014, new { seconds = 2.8, scale = scale.ScaleX });
                await Task.Delay(2800);
                Check("away icon returns at full 5.6s", Math.Abs(scale.ScaleX - 1) < .014, new { seconds = 5.6, scale = scale.ScaleX });
                for(int cycle=2;cycle<=3;cycle++)
                {
                    await Task.Delay(2800);Check("away cycle "+cycle+" maximum",Math.Abs(scale.ScaleX-1.08)<.014,new{scale=scale.ScaleX});
                    await Task.Delay(2800);Check("away cycle "+cycle+" return",Math.Abs(scale.ScaleX-1)<.014,new{scale=scale.ScaleX});
                }
                state.Settings.ReduceMotion = true;
                typeof(IslandWindow).GetField("visualState", Flags)!.SetValue(window, "");
                Render();
                var atStart = scale.ScaleX;
                await Task.Delay(350);
                Check("reduce motion stops icon animation", scale.ScaleX == atStart && !scale.HasAnimatedProperties);
                var host=(Grid)typeof(IslandWindow).GetField("PageHost",Flags)!.GetValue(window)!;
                var matrix=new PixelProgress {Width=200,Height=24,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};
                host.Children.Add(matrix);window.UpdateLayout();
                var brush=new SolidColorBrush(Color.FromRgb(64,255,97));
                int Count()=>(int)typeof(PixelProgress).GetField("lastCount",Flags)!.GetValue(matrix)!;
                matrix.Update(.000001,"tracking","matrix",brush,false,30);Check("tiny positive main matrix remains zero cells",Count()==0);
                matrix.Update(.99,"tracking","matrix",brush,false,30);Check("main matrix .99 fills 89 cells",Count()==89);
                matrix.Update(1,"tracking","matrix",brush,false,30);Check("completed tracking fills 90 and scans",Count()==90&&matrix.ScanAnimating);
                matrix.Update(.5,"away","matrix",brush,false,30,false);Check("before ten seconds away matrix is static",!matrix.PulseAnimating&&!matrix.ScanAnimating);
                matrix.Update(.5,"away","matrix",brush,false,30,true);Check("eligible unfinished away matrix breathes",matrix.PulseAnimating&&!matrix.ScanAnimating);
                var unfilled=(System.Windows.Shapes.Path)typeof(PixelProgress).GetField("unfilled",Flags)!.GetValue(matrix)!;
                for(int cycle=1;cycle<=3;cycle++)
                {
                    await Task.Delay(1400);Check("matrix away cycle "+cycle+" bright",Math.Abs(unfilled.Opacity-.35)<.015,new{opacity=unfilled.Opacity});
                    await Task.Delay(1400);Check("matrix away cycle "+cycle+" dim",Math.Abs(unfilled.Opacity-.10)<.015,new{opacity=unfilled.Opacity});
                }
                matrix.Update(1,"away","matrix",brush,false,30,true);Check("completed away matrix is static",!matrix.PulseAnimating&&!matrix.ScanAnimating);
                matrix.Update(.5,"tracking","matrix",brush,false,30);await Task.Delay(500);
                var scan=(TranslateTransform)typeof(PixelProgress).GetField("scan",Flags)!.GetValue(matrix)!;var before=scan.X;
                matrix.Update(.6,"tracking","matrix",brush,false,30);await Task.Delay(100);Check("progress update preserves scan phase",scan.X>before,new{before,after=scan.X});
                brush.Color=Colors.DodgerBlue;
                var band=(System.Windows.Shapes.Rectangle)typeof(PixelProgress).GetField("filledBand",Flags)!.GetValue(matrix)!;
                Check("scan band follows new accent without stale orange",((LinearGradientBrush)band.Fill).GradientStops[3].Color.B==Colors.DodgerBlue.B);
                matrix.Update(.5,"tracking","matrix",brush,true,30);Check("reduced motion stops matrix scan",!matrix.ScanAnimating&&!matrix.PulseAnimating);
                var mini=new HistoryMatrix {Progress=.000001};Check("history positive progress uses at least one cell",mini.FilledCells==1);mini.Progress=.99;Check("unfinished history reserves final cell",mini.FilledCells==9);
                window.Activate();
                Check("real test HWND obtains focus",await Until(()=>window.IsActive));
                var expand=(Button)typeof(IslandWindow).GetField("ExpandButton",Flags)!.GetValue(window)!;
                DependencyObject? OrbAncestor(Point local)
                {
                    var hit=window.InputHitTest(expand.TranslatePoint(local,window)) as DependencyObject;
                    while(hit is not null && !ReferenceEquals(hit,expand))hit=VisualTreeHelper.GetParent(hit);
                    return hit;
                }
                Check("orb center owns click hit",ReferenceEquals(OrbAncestor(new Point(expand.ActualWidth/2,expand.ActualHeight/2)),expand));
                Check("orb corner rejects click without clipping glow",OrbAncestor(new Point(0,0)) is null && expand.Clip is null);
                expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check("orb routed click opens navigation",(bool)typeof(IslandWindow).GetField("isExpanded",Flags)!.GetValue(window)!);
                var probe=new Window {Title="Work Island isolated focus probe",Width=80,Height=30,ShowInTaskbar=false,WindowStyle=WindowStyle.ToolWindow,Left=window.Left+window.Width+8,Top=window.Top};
                try
                {
                    var focusTimer=(DispatcherTimer)typeof(IslandWindow).GetField("focusReturn",Flags)!.GetValue(window)!;
                    bool Expanded()=>(bool)typeof(IslandWindow).GetField("isExpanded",Flags)!.GetValue(window)!;
                    var focusWatch=new Stopwatch();double? lossAt=null;string focusCase="return";
                    void LogFocus(string stage)=>focusEvents.Add(new{test=focusCase,stage,elapsedMs=focusWatch.Elapsed.TotalMilliseconds,expanded=Expanded(),timerEnabled=focusTimer.IsEnabled,windowActive=window.IsActive,probeActive=probe.IsActive});
                    window.Deactivated+=(_,_)=>{lossAt=focusWatch.Elapsed.TotalMilliseconds;LogFocus("deactivated");};
                    window.Activated+=(_,_)=>LogFocus("activated");focusTimer.Tick+=(_,_)=>LogFocus("return tick");
                    async Task<bool> ResetFocusCase()
                    {
                        focusTimer.Stop();typeof(IslandWindow).GetMethod("CollapseToTimer",Flags)!.Invoke(window,null);window.Activate();
                        if(!await Until(()=>window.IsActive))return false;
                        await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                        focusTimer.Stop();expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        return Expanded() && !focusTimer.IsEnabled;
                    }
                    probe.Show();var resetReturn=await ResetFocusCase();lossAt=null;focusWatch.Restart();probe.Activate();
                    var sawLoss=await Until(()=>lossAt.HasValue && !window.IsActive && probe.IsActive);
                    var returned=await Until(()=>!Expanded());
                    var returnMs=lossAt.HasValue?focusWatch.Elapsed.TotalMilliseconds-lossAt.Value:(double?)null;
                    LogFocus("return assertion");
                    Check("actual own-window Deactivated event returns to timer",resetReturn && sawLoss && returned && returnMs>=900 && returnMs<=4000,new{returnMs,timeoutMs=4000,windowActive=window.IsActive,probeActive=probe.IsActive});
                    focusCase="cancel";var resetCancel=await ResetFocusCase();lossAt=null;focusWatch.Restart();probe.Activate();
                    var cancellationLoss=await Until(()=>lossAt.HasValue && !window.IsActive && focusTimer.IsEnabled);
                    await Task.Delay(200);window.Activate();var regained=await Until(()=>window.IsActive && !focusTimer.IsEnabled);
                    var observation=Stopwatch.StartNew();var retained=Expanded();
                    while(observation.ElapsedMilliseconds<2000 && retained){await Task.Delay(25);retained=Expanded() && window.IsActive && !focusTimer.IsEnabled;}
                    LogFocus("cancellation assertion");
                    Check("actual Activated event cancels focus return",resetCancel && cancellationLoss && regained && retained,new{observedMs=observation.Elapsed.TotalMilliseconds,regained,resetCancel});
                    focusCase="tray-menu";
                    var resetMenu=await ResetFocusCase();
                    var notifyIcon=(System.Windows.Forms.NotifyIcon)typeof(IslandWindow).GetField("tray",Flags)!.GetValue(window)!;
                    var nativeMenu=notifyIcon.ContextMenuStrip!;
                    // Keep this controlled popup visible while the independent focus probe
                    // activates, so we test deferral rather than WinForms auto-dismissal.
                    nativeMenu.AutoClose=false;
                    nativeMenu.Show(new System.Drawing.Point((int)(window.Left+window.Width+100),(int)window.Top+60));
                    probe.Activate();
                    var menuLoss=await Until(()=>!window.IsActive && probe.IsActive);
                    await Task.Delay(1400);
                    Check("native tray menu retains expanded page during external focus",resetMenu && menuLoss && nativeMenu.Visible && Expanded(),new{resetMenu,menuLoss,menuVisible=nativeMenu.Visible,expanded=Expanded()});
                    nativeMenu.Close();
                    nativeMenu.AutoClose=true;
                    Check("closing native tray menu restarts deferred focus return",await Until(()=>!Expanded()));
                    focusTimer.Stop();typeof(IslandWindow).GetMethod("CollapseToTimer",Flags)!.Invoke(window,null);
                }
                finally {probe.Close();}
            }
            catch (Exception error)
            {
                failures++;
                results.Add(new { name = "regression harness", passed = false, error = error.ToString() });
            }
            finally
            {
                if (window is not null)
                {
                    typeof(IslandWindow).GetField("exiting", Flags)!.SetValue(window, true);
                    window.Close();
                }
                try
                {
                    File.WriteAllText(Path.Combine(output, "app-regressions.json"), JsonSerializer.Serialize(new
                    {
                        passed = failures == 0,
                        checks = results,
                        focusEvents,
                        limitation = "Invokes simulated system notification handlers and reads actual WPF animated properties. Does not lock or suspend Windows; uses isolated data."
                    }, AtomicStore.JsonOptions));
                }
                catch(Exception error)
                {
                    failures++;
                    File.WriteAllText(Path.Combine(output,"report-error.txt"),error.ToString());
                    File.WriteAllText(Path.Combine(output,"app-regressions.json"),JsonSerializer.Serialize(new{passed=false,error="Evidence serialization failed; see report-error.txt"},AtomicStore.JsonOptions));
                }
                finally{app.Shutdown();}
            }
        });
        app.Run();
        Console.WriteLine(File.ReadAllText(Path.Combine(output, "app-regressions.json")));
        return failures == 0 ? 0 : 1;
    }
}
