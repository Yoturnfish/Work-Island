using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WorkIsland.Core;

namespace WorkIsland.App;
public partial class IslandWindow
{
    private bool loadingPreferences;
    private bool opacityPreview;
    private bool pointerInside;
    private DateTimeOffset? awaySince;
    private string elapsedState = "";
    private string historyMode = "list";
    private string? detailDay;
    private readonly DispatcherTimer focusReturn = new() { Interval = TimeSpan.FromSeconds(1) };
    private ComboBox[] PreferencePickers=>[DisplayPicker,BackgroundPicker,DurationPicker,GoalPicker,IdlePicker,AwayPicker,ReminderPicker,LanguagePicker];
    private readonly List<(TextBlock Label,string Key)> interfaceLabels=[];
    private string appliedInterfaceLanguage="";
    private string reminderKey = "";
    private AnimationClock? reminderClock;
    private DateTimeOffset? reminderResetAt,reminderPreviewStartedAt;
    private bool reminderOnboardingActive;
    private bool preferenceSaveFailed;
    private string historySignature = "";
    private HistoryStatisticsStrip? liveHistoryStrip;
    private static readonly Dictionary<string, string> English = new()
    {
        ["屏幕工作时间"]="Screen work time", ["设置"]="Settings", ["历史"]="History", ["识别"]="Rules",
        ["登录后启动"]="Launch at login", ["显示模式"]="Display mode", ["背景样式"]="Background",
        ["黑色"]="Black", ["实灰回退"]="Solid fallback", ["玻璃"]="Glass", ["透明"]="Transparent", ["悬浮窗模式"]="Floating island", ["菜单栏模式"]="Menu bar", ["透明度"]="Opacity",
        ["时间格式"]="Time format", ["每日目标"]="Daily goal", ["进入游离状态"]="Away after", ["游离计时"]="Away timer",
        ["超级提醒"]="Reminder", ["每日刷新"]="Day reset", ["语言"]="Language", ["颜色组合"]="Accent",
        ["进度样式"]="Progress style", ["窗口缩放"]="Scale", ["保持窗口置顶"]="Keep on top", ["减少动态效果"]="Reduce motion",
        ["收起到系统托盘"]="Hide to tray", ["应用识别"]="Application rules", ["矩形矩阵"]="Matrix", ["波形进度条"]="Waveform",
        ["暂停计时"]="Pause", ["继续计时"]="Resume", ["历史统计"]="History statistics", ["修改后回车保存"]="Enter to save",
        ["关"]="Off", ["开"]="On", ["闪光"]="Flash", ["绿色"]="Green", ["蓝色"]="Blue", ["黄色"]="Yellow", ["青色"]="Cyan", ["粉色"]="Pink", ["紫色"]="Purple",["浅粉色"]="Soft pink",
        ["默认"]="Default",["命名"]="Name",["新组合"]="New theme",["新建组合"]="New theme",["删除组合"]="Delete theme",["确认删除"]="Confirm deletion",["未选择组合"]="No theme selected",["删除当前组合？历史记录保留。"]="Delete this theme? History is retained.",
        ["切换颜色可重新配置识别组合，适应不同工作类型。"]="Choose a color to configure rules for another kind of work.",["为颜色组合命名"]="Name this theme",
        ["选择当前网页"]="Choose current page",["当前网页"]="Current pages",["当前应用程序"]="Running applications",
        ["正在读取浏览器当前页…"]="Reading current browser pages…",["上一次应用读取尚未结束，请稍后重试"]="Application scan is still running. Try again shortly.",
        ["应用列表读取超时，请稍后重试"]="Application scan timed out. Try again.",["未找到可添加的应用"]="No applications found",
        ["上一次读取尚未结束，请稍后重试"]="The previous scan is still running. Try again shortly.",["浏览器暂未响应，请稍后重新识别"]="Browser did not respond. Try again.",
        ["未找到已打开的浏览器"]="No supported browsers are open",["暂时无法读取，请重新识别。"]="Unable to read. Try auto-detect again.",
        ["当前页地址不可读取"]="Current page address is unavailable",["已添加"]="Added",["已移除"]="Removed",
        ["此网页暂不支持自动添加"]="This page cannot be added automatically",
        ["保存失败，更改暂留内存。请检查存储位置。"]="Save failed; changes remain in memory. Check storage.",["更改已保存。"]="Changes saved.",
        ["自动识别"]="Auto-detect",["应用程序"]="Applications",["网站"]="Websites",["退出 Work Island"]="Quit Work Island",
        ["显示 Work Island"]="Show Work Island",["暂停 / 恢复"]="Pause / Resume",["退出"]="Quit",
        ["这个应用已在列表中。勾选它即可启用。"]="This app is already listed. Select it to enable tracking.",
        ["这个网站已在列表中。勾选它即可启用。"]="This website is already listed. Select it to enable tracking.",
        ["请选择有效的应用程序（.exe）。"]="Choose a valid application (.exe).",["未能添加应用，请重新选择。"]="Could not add this app. Choose it again.",
        ["请输入网站域名或关键词，例如 github.com。"]="Enter a website domain or keyword, such as github.com.",
        ["请输入有效的进程名，例如 code.exe。"]="Enter a valid process name, such as code.exe.",
        ["不支持此浏览器"]="This browser is not supported",["窗口已关闭"]="The window has closed",["隐私窗口不读取"]="Private windows are not read",
        ["地址正在编辑，请完成导航后再识别"]="Finish editing the address before detecting this page",["窗口已变化"]="The window has changed",
        ["浏览器暂未提供地址"]="The browser has not provided an address",
        ["选择需要识别的应用"]="Choose an app to track",["应用程序 (*.exe)"]="Applications (*.exe)",
        ["返回导航"]="Back",["打开导航"]="Open navigation",["暂停或恢复计时"]="Pause or resume timing",
        ["选择并添加应用程序"]="Choose and add an app",["选择并添加当前网页"]="Choose and add the current page",
        ["工作进程名"]="Work process name",["网站域名或关键词"]="Website domain or keyword",
        ["小时"]="Hours",["分钟"]="Minutes"
    };
    private string Text(string chinese) => engine.Data.Settings.Language == "en" && English.TryGetValue(chinese, out var value) ? value : chinese;
    private void CaptureInterfaceLabels()
    {
        // Capture only declarative interface labels, before application names or
        // user-created content exist. Never infer a translation key from data.
        void Visit(DependencyObject root)
        {
            if(root is TextBlock text&&English.ContainsKey(text.Text))interfaceLabels.Add((text,text.Text));
            foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())Visit(child);
        }
        Visit(PageHost);
    }
    private void RefreshTrayLanguage()
    {
        if(tray?.ContextMenuStrip is not {} menu)return;
        menu.Items[0].Text=Text("显示 Work Island");menu.Items[1].Text=Text("暂停 / 恢复");menu.Items[3].Text=Text("退出");
    }
    private Color ThemeColor => engine.Data.Settings.Accent switch
    {
        "blue" => Color.FromRgb(51,140,255), "yellow" => Color.FromRgb(255,214,10), "cyan" => Color.FromRgb(50,230,230),
        "pink" => Color.FromRgb(255,55,95),"orange"=>Color.FromRgb(255,153,194), "purple" => Color.FromRgb(191,90,242), _ => Color.FromRgb(64,255,97)
    };
    private void InitializeVisualPreferences()
    {
        loadingPreferences = true;
        foreach (var pair in new (ComboBox Picker,string Value)[]
        {
            (DisplayPicker,engine.Data.Settings.DisplayMode), (BackgroundPicker,engine.Data.Settings.BackgroundStyle),
            (DurationPicker,engine.Data.Settings.DurationStyle), (GoalPicker,(engine.Data.Settings.GoalSeconds/3600).ToString(CultureInfo.InvariantCulture)),
            (IdlePicker,engine.Data.Settings.AwayThreshold.ToString(CultureInfo.InvariantCulture)), (AwayPicker,engine.Data.Settings.AwayTimerEnabled ? "true":"false"),
            (ReminderPicker,engine.Data.Settings.SuperReminderEnabled ? "true":"false"), (LanguagePicker,engine.Data.Settings.Language)
        })
        {
            var selected=pair.Picker.Items.Cast<ComboBoxItem>().FirstOrDefault(item=>Equals(item.Tag?.ToString(),pair.Value));
            if(selected is null && (pair.Picker==GoalPicker || pair.Picker==IdlePicker))
            {
                selected=new(){Tag=pair.Value,Content=pair.Value+(pair.Picker==GoalPicker ? " 小时":" 秒")};pair.Picker.Items.Add(selected);
            }
            pair.Picker.SelectedItem=selected;
        }
        OpacitySlider.Value = engine.Data.Settings.BackgroundOpacity;
        OpacitySlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_,_)=> { opacityPreview=true; ApplyWindowOpacity(); }));
        OpacitySlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_,_)=> { opacityPreview=false; ApplyWindowOpacity(); }));
        loadingPreferences = false;
        foreach(var picker in PreferencePickers)
            picker.DropDownClosed+=(_,_)=> {if(!IsActive && !smoke)BeginFocusReturn();};
        ApplyMaterial(); ApplyLanguage();
        MouseEnter += (_,_)=>NotifyPointer(true); MouseLeave += (_,_)=>NotifyPointer(false);
        Activated += (_,_)=>focusReturn.Stop();
        Deactivated += (_,_)=> { if (!smoke) BeginFocusReturn(); };
        focusReturn.Tick += (_,_)=>
        {
            focusReturn.Stop();
            if (!OnboardingActive && isExpanded && !HasOpenPicker()) CollapseToTimer();
        };
        Closed += (_,_)=>focusReturn.Stop();
    }
    private bool HasOpenPicker() => onboardingGoal?.IsDropDownOpen==true || PreferencePickers.Any(p=>p.IsDropDownOpen)
        || RecognitionColorButton.ContextMenu?.IsOpen==true || recognitionPicker?.IsOpen==true || applicationChooserOpen || tray?.ContextMenuStrip?.Visible==true;
    private void BeginFocusReturn() { if(OnboardingActive)return; if(IsMenuBar){MenuFocusReturn();return;} if (!fullscreenSuppressed && IsVisible && isExpanded && !HasOpenPicker()) { focusReturn.Stop(); focusReturn.Start(); } }
    private void NotifyPointer(bool value) { pointerInside=value;if(!value)ReleaseOnboardingOpacityOutside(Mouse.GetPosition(this));ApplyWindowOpacity(); }
    private void CollapseToTimer() { if(OnboardingActive)return; if(IsMenuBar){HideMenuPanel();return;} CloseRecognitionPicker(); if (!isExpanded) return; isExpanded=false; Transition(DetailsPanel,CompactPanel,false); ApplyMaterial(); ApplyWindowOpacity(); }
    private void ApplyWindowOpacity()
    {
        if (engine is null) return;
        var settings=engine.Data.Settings;
        SetPresentedWindowOpacity(OnboardingActive || holdOnboardingOpacity || IsMenuBar || settings.BackgroundStyle=="glass" ? 1 : opacityPreview ? settings.BackgroundOpacity
            : isExpanded ? 1 : settings.BackgroundOpacity);
        OpacityLabel.Text = $"{settings.BackgroundOpacity:P0}";
        surfaces?.UpdateMaterialGlass(MaterialGlassAmount);
    }
    private void ApplyMaterial()
    {
        var glass=engine.Data.Settings.BackgroundStyle=="glass"&&!OnboardingActive;
        UpdateMaterialPresentation();
        Outline.BorderThickness=new Thickness(engine.Data.Settings.IslandScale);
        OpacityRow.Visibility=glass || IsMenuBar ? Visibility.Collapsed:Visibility.Visible;
        ApplyWindowOpacity();
        var glassItem=BackgroundPicker.Items.Cast<ComboBoxItem>().First(i=>i.Tag?.ToString()=="glass");
        glassItem.Content=Text("透明");
    }
    private void VisualPreferenceChanged(object sender,SelectionChangedEventArgs e)
    {
        if(engine is null || loadingPreferences || sender is not ComboBox picker || picker.SelectedItem is not ComboBoxItem item)return;
        var value=item.Tag?.ToString() ?? "";
        if(picker==DisplayPicker){ChangeDisplayMode(value);return;}
        if(picker==GoalPicker||picker==IdlePicker)
        {
            // Tags are stable numeric identifiers, independent of the user's
            // decimal separator. Preserve the untouched model value exactly;
            // a formatted backing editor is not the source of another setting.
            if(!double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var number))return;
            SaveTrackingPreferences(picker==GoalPicker?number*3600:engine.Data.Settings.GoalSeconds,
                picker==IdlePicker?number:engine.Data.Settings.AwayThreshold);
            return;
        }
        var previous=VisualPreferenceValue(picker);
        if(previous is null||previous==value)return;
        SetVisualPreference(picker,value);
        if(!Persist())
        {
            SetVisualPreference(picker,previous);RestorePreferencePicker(picker,previous);ShowPreferenceSaveFailure();return;
        }
        if(picker==ReminderPicker)ResetReminder(value=="true");
        PreferenceSaveSucceeded();ApplyMaterial();ApplyLanguage();Render();
    }
    private string? VisualPreferenceValue(ComboBox picker)
    {
        var settings=engine.Data.Settings;
        return picker==BackgroundPicker?settings.BackgroundStyle:picker==DurationPicker?settings.DurationStyle
            :picker==AwayPicker?settings.AwayTimerEnabled?"true":"false":picker==ReminderPicker?settings.SuperReminderEnabled?"true":"false"
            :picker==LanguagePicker?settings.Language:null;
    }
    private void SetVisualPreference(ComboBox picker,string value)
    {
        var settings=engine.Data.Settings;
        if(picker==BackgroundPicker)settings.BackgroundStyle=value;
        else if(picker==DurationPicker)settings.DurationStyle=value;
        else if(picker==AwayPicker)settings.AwayTimerEnabled=value=="true";
        else if(picker==ReminderPicker)settings.SuperReminderEnabled=value=="true";
        else if(picker==LanguagePicker)settings.Language=value;
    }
    private void RestorePreferencePicker(ComboBox picker,string value)
    {
        var wasLoading=loadingPreferences;loadingPreferences=true;
        try
        {
            var item=picker.Items.Cast<ComboBoxItem>().FirstOrDefault(i=>i.Tag?.ToString()==value);
            if(item is null)
            {
                item=new(){Tag=value,Content=value+(picker==GoalPicker?(engine.Data.Settings.Language=="en"?" hours":" 小时"):(engine.Data.Settings.Language=="en"?" sec":" 秒"))};picker.Items.Add(item);
            }
            picker.SelectedItem=item;
        }
        finally{loadingPreferences=wasLoading;}
    }
    private void RestoreTrackingPreferenceControls()
    {
        LoadSettingsInputs();RestorePreferencePicker(GoalPicker,(engine.Data.Settings.GoalSeconds/3600).ToString(CultureInfo.InvariantCulture));
        RestorePreferencePicker(IdlePicker,engine.Data.Settings.AwayThreshold.ToString(CultureInfo.InvariantCulture));
    }
    private void ShowPreferenceSaveFailure()
    {
        preferenceSaveFailed=true;
        ShowError(engine.Data.Settings.Language=="en"?"Save failed. Unchanged; retry.":"保存失败，设置未更改，请重试。");
        // BringIntoView can align the last text row exactly with the rounded
        // window edge. Leave breathing room after the new label is measured.
        Dispatcher.BeginInvoke(()=>
        {
            if(!preferenceSaveFailed||!MessageLabel.IsVisible||!ReferenceEquals(activePage,SettingsPage))return;
            MessageLabel.UpdateLayout();
            var bottom=MessageLabel.TranslatePoint(new Point(0,MessageLabel.ActualHeight),PageScroller).Y;
            PageScroller.ScrollToVerticalOffset(PageScroller.VerticalOffset+Math.Max(0,bottom-(PageScroller.ActualHeight-8)));
        },DispatcherPriority.Loaded);
    }
    private void PreferenceSaveSucceeded()
    {
        if(!preferenceSaveFailed)return;
        preferenceSaveFailed=false;MessageLabel.Text=engine.Data.Settings.Language=="en"?"Settings saved.":"设置已保存。";
    }
    private void OpacityChanged(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(engine is null || loadingPreferences)return;
        var previous=engine.Data.Settings.BackgroundOpacity;engine.Data.Settings.BackgroundOpacity=OpacitySlider.Value;
        if(IsLoaded&&!Persist())
        {
            engine.Data.Settings.BackgroundOpacity=previous;var wasLoading=loadingPreferences;loadingPreferences=true;
            try{OpacitySlider.Value=previous;}finally{loadingPreferences=wasLoading;}
            ShowPreferenceSaveFailure();return;
        }
        PreferenceSaveSucceeded();ApplyWindowOpacity();
    }
    private void SwitchColorProfile(string accent)
    {
        CloseRecognitionPicker();
        var settings=engine.Data.Settings;
        if(!smoke)Tick();engine.ResetObservation();
        ColorProfilesService.SelectAccent(settings,accent);
        RebuildRules();
    }
    private void ResetTimeChanged(object? sender,EventArgs e)
    {
        if(engine is null || engine.Data.Settings.DayResetMinutes==ResetInput.Minutes)return;
        // Checkpoint under the old boundary; save the new preference and date
        // grouping together. Existing durations and session identities are kept.
        if(!smoke&&!suspended)RefreshActivityObservation();
        if(!engine.Data.Settings.Paused)engine.ResetObservation();
        var previousMinutes=engine.Data.Settings.DayResetMinutes;var nextMinutes=ResetInput.Minutes;
        var records=engine.Data.Sessions.ToArray();var previousDays=records.Select(s=>s.DayKey).ToArray();
        string[] nextDays;
        try{nextDays=records.Select(s=>LogicalDay.Key(s.StartedAt,engine.Zone,nextMinutes)).ToArray();}
        catch(ArgumentOutOfRangeException)
        {
            ResetInput.RestoreMinutes(previousMinutes);
            ShowError(engine.Data.Settings.Language=="en"?"Daily reset time was not changed: a historical date cannot be regrouped.":"刷新时间未更改：部分历史日期无法重新归类。");return;
        }
        engine.Data.Settings.DayResetMinutes=nextMinutes;
        for(var i=0;i<records.Length;i++)records[i].DayKey=nextDays[i];
        if(!Persist())
        {
            engine.Data.Settings.DayResetMinutes=previousMinutes;
            for(var i=0;i<records.Length;i++)records[i].DayKey=previousDays[i];
            ResetInput.RestoreMinutes(previousMinutes);
        }
        Render();
    }
    private void ApplyLanguage()
    {
        RefreshLoginFeedbackLanguage();
        RefreshSaveFeedbackLanguage();
        if(appliedInterfaceLanguage!=engine.Data.Settings.Language)
        {
            appliedInterfaceLanguage=engine.Data.Settings.Language;CloseRecognitionPicker();
            if(RecognitionColorButton.ContextMenu is {} colorMenu)colorMenu.IsOpen=false;
            if(!OnboardingActive&&!string.IsNullOrEmpty(recognitionMessageKey))RecognitionMessage(recognitionMessageKey);
        }
        foreach(var (label,key) in interfaceLabels)label.Text=Text(key);
        foreach(var picker in PreferencePickers)
        {
            foreach(var item in picker.Items.Cast<ComboBoxItem>())
            {
                var tag=item.Tag?.ToString() ?? "";
                string? chinese=tag switch { "floating"=>"悬浮窗模式","menuBar"=>"菜单栏模式","black"=>"黑色","glass"=>"透明","false"=>"关","true"=>picker==ReminderPicker ? "闪光":"开","green"=>"绿色","blue"=>"蓝色","yellow"=>"黄色","cyan"=>"青色","pink"=>"粉色","orange"=>"浅粉色","purple"=>"紫色",_=>null};
                if(chinese is not null)item.Content=Text(chinese);
                if(picker==GoalPicker)item.Content=tag+(engine.Data.Settings.Language=="en"?" hours":" 小时");
                if(picker==IdlePicker && int.TryParse(tag,out var seconds))item.Content=seconds<60 ? seconds+(engine.Data.Settings.Language=="en"?" sec":" 秒") : seconds/60+(engine.Data.Settings.Language=="en"?" min":" 分钟");
            }
        }
        LoginCheck.Content=Text("登录后启动");
        var english=engine.Data.Settings.Language=="en";
        foreach(var nav in new[]{SettingsNavigationButton,HistoryNavigationButton,RecognitionNavigationButton})
            if(nav.Content is StackPanel stack && stack.Children.OfType<TextBlock>().LastOrDefault() is {} label)label.FontSize=english?10:12;
        foreach(var (element,key) in new (DependencyObject,string)[]{(PageBackButton,"返回导航"),(ExpandButton,"打开导航"),(PauseButton,"暂停或恢复计时"),(AddApplicationSelectionButton,"选择并添加应用程序"),(AddWebsiteSelectionButton,"选择并添加当前网页"),(RuleInput,"工作进程名"),(WebsiteInput,"网站域名或关键词")})AutomationProperties.SetName(element,Text(key));
        RefreshRecognitionHeaders();RefreshTrayLanguage();
        historySignature=""; RefreshHistory();
        RefreshOnboardingLabels();
    }
    private void RenderVisualExtras(DateTimeOffset now,bool reduced)
    {
        if(reminderOnboardingActive!=OnboardingActive)
        {
            reminderOnboardingActive=OnboardingActive;reminderPreviewStartedAt=null;reminderResetAt=null;
            elapsedState="";awaySince=null;reminderKey="";StopReminderMotion();
        }
        if(elapsedState!=engine.Current.State)
        {
            elapsedState=engine.Current.State;awaySince=elapsedState=="away"&&!OnboardingActive?now:null;
        }
        var elapsed=awaySince is {} start ? Math.Max(0,(now-start).TotalSeconds):0;
        if(engine.Data.Settings.AwayTimerEnabled && elapsedState=="away")
        {
            var seconds=(long)elapsed;TimeLabel.Text=$"{seconds/60:00}M {seconds%60:00}S";
        }
        UpdateReminderMotion(now,reduced);
        PauseButton.Background=new SolidColorBrush(ThemeColor);
        RefreshNavigationPalette();
        RecognitionColorButton.Background=new SolidColorBrush(ThemeColor);
        ClockHands.Stroke=new SolidColorBrush(ThemeColor);
        TitleLabel.Text=string.IsNullOrWhiteSpace(engine.Data.Settings.DisplayTitle)?Text("屏幕工作时间"):engine.Data.Settings.DisplayTitle;
        PauseActionText.Text=Text(engine.Data.Settings.Paused?"继续计时":"暂停计时");
        RefreshHistory();
    }
    private void StopReminderMotion()
    {
        reminderClock?.Controller?.Remove();reminderClock=null;ReminderGlow.BeginAnimation(OpacityProperty,null);ReminderGlow.Opacity=0;
    }
    private void ResetReminder(bool preview)
    {
        var now=DateTimeOffset.Now;reminderResetAt=now;
        reminderPreviewStartedAt=preview&&!OnboardingActive?now:null;
        reminderKey="";StopReminderMotion();
    }
    private void UpdateReminderMotion(DateTimeOffset now,bool reduced)
    {
        if(reminderPreviewStartedAt is {} deadline&&(now-deadline).TotalSeconds>=1.5)reminderPreviewStartedAt=null;
        var preview=false;var elapsed=0d;var key="";var settings=engine.Data.Settings;
        if(settings.SuperReminderEnabled&&!reduced&&!suspended&&!OnboardingActive)
        {
            if(reminderPreviewStartedAt is {} selected)
            {
                preview=true;elapsed=Math.Max(0,(now-selected).TotalSeconds);key=$"preview/{selected:O}";
            }
            else if(elapsedState=="away"&&engine.Progress(now)<1&&awaySince is {} away)
            {
                var start=reminderResetAt is {} reset&&reset>away?reset:away;
                elapsed=Math.Max(0,(now-start).TotalSeconds);
                if(elapsed>=settings.AwayThreshold)key=$"away/{start:O}/{settings.AwayThreshold}";
            }
        }
        if(key==reminderKey)return;
        SuspendNativeForeground();reminderKey=key;StopReminderMotion();
        if(key.Length==0)return;
        var animation=new ReminderAnimation {Preview=preview,Threshold=settings.AwayThreshold,ElapsedOffset=elapsed,
            Duration=new Duration(preview?TimeSpan.FromSeconds(Math.Max(.001,1.5-elapsed)):TimeSpan.FromDays(365)),
            RepeatBehavior=preview?new RepeatBehavior(1):RepeatBehavior.Forever,FillBehavior=FillBehavior.Stop};
        // Mac's SuperReminderView requests one frame per 1/30 second. Bound
        // this clock independently; keep pulse duration and strength unchanged.
        Timeline.SetDesiredFrameRate(animation,30);
        var clock=(AnimationClock)animation.CreateClock(true);
        reminderClock=clock;
        if(preview)clock.Completed+=(_,_)=>
        {
            if(!ReferenceEquals(reminderClock,clock)||exiting)return;
            reminderPreviewStartedAt=null;reminderKey="";StopReminderMotion();Render();
        };
        ReminderGlow.ApplyAnimationClock(OpacityProperty,clock);clock.Controller!.Begin();
    }
    private bool UnfinishedAwayBreathing(DateTimeOffset now) => awaySince is {} start && (now-start).TotalSeconds>=10;
    private void ToggleApplicationRules(object sender,RoutedEventArgs e) {RuleRows.Visibility=RuleRows.Visibility==Visibility.Visible ? Visibility.Collapsed:Visibility.Visible;RefreshRecognitionHeaders(true);}
    private void OpenColorMenu(object sender,RoutedEventArgs e)
        => OpenProfilePopover();
    private string CompactTime(double total) => DurationText.Compact(total,engine.Data.Settings.DurationStyle);
    private void RefreshHistory()
    {
        if(historyMode=="statistics"&&liveHistoryStrip is {} strip&&ReferenceEquals(activePage,HistoryPage)&&IsVisible)
        {
            var currentDay=LogicalDay.Key(DateTimeOffset.Now,engine.Zone,engine.Data.Settings.DayResetMinutes);
            var total=0d;var hasRecords=false;
            foreach(var record in engine.Data.Sessions)if(record.DayKey==currentDay){hasRecords=true;total+=record.EffectiveSeconds;}
            strip.UpdateCurrentDay(DateOnly.ParseExact(currentDay,"yyyy-MM-dd",CultureInfo.InvariantCulture),total,hasRecords);
            return;
        }
        if(historyMode!="list")return;
        liveHistoryStrip=null;
        var day=DateOnly.ParseExact(LogicalDay.Key(DateTimeOffset.Now,engine.Zone,engine.Data.Settings.DayResetMinutes),"yyyy-MM-dd",CultureInfo.InvariantCulture);
        var summaries=Enumerable.Range(0,7).Select(i=> {var date=day.AddDays(-i);var key=date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);return (key,seconds:engine.Data.Sessions.Where(s=>s.DayKey==key).Sum(s=>s.EffectiveSeconds));}).ToArray();
        var signature=engine.Data.Settings.Language+"/"+engine.Data.Settings.Accent+"/"+engine.Data.Settings.GoalSeconds+"/"+string.Join(";",summaries.Select(s=>s.key+":"+DurationText.WholeKey(s.seconds)));
        if(signature==historySignature)return;historySignature=signature;HistoryRows.Children.Clear();
        var stats=new Button {Height=24,Margin=new Thickness(6,0,6,0),HorizontalContentAlignment=HorizontalAlignment.Stretch};
        var header=new DockPanel();var chevron=VectorIcon.Make("chevron",new SolidColorBrush(Color.FromArgb(173,255,255,255)),14);DockPanel.SetDock(chevron,Dock.Right);header.Children.Add(chevron);
        var chart=VectorIcon.Make("chart",new SolidColorBrush(Color.FromArgb(173,255,255,255)),16);chart.Margin=new Thickness(0,0,8,0);header.Children.Add(chart);
        header.Children.Add(new TextBlock {Text=Text("历史统计"),FontSize=13,FontWeight=FontWeights.SemiBold});stats.Content=header;stats.Click+=(_,_)=>ShowHistoryStatistics();HistoryRows.Children.Add(stats);
        foreach(var summary in summaries)
        {
            var row=new Grid {Height=24,Margin=new Thickness(6,0,6,0)};
            row.ColumnDefinitions.Add(new(){Width=new GridLength(66)});row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var matrix=new HistoryMatrix {Width=58,Height=4,Progress=summary.seconds/engine.Data.Settings.GoalSeconds,Accent=ThemeColor,VerticalAlignment=VerticalAlignment.Center};
            var date=new TextBlock {Text=summary.key,FontSize=13,FontFamily=new FontFamily("Consolas"),FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};
            var time=new TextBlock {Text=CompactTime(summary.seconds),FontSize=13,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(ThemeColor),VerticalAlignment=VerticalAlignment.Center};
            Grid.SetColumn(date,1);Grid.SetColumn(time,2);row.Children.Add(matrix);row.Children.Add(date);row.Children.Add(time);
            var surface=new Border {CornerRadius=new CornerRadius(8),Background=new SolidColorBrush(Color.FromArgb(14,255,255,255)),Child=row};
            var button=new Button {Content=surface,Margin=new Thickness(6,3,6,0),HorizontalContentAlignment=HorizontalAlignment.Stretch};button.Click+=(_,_)=>ShowHistoryDetail(summary.key);HistoryRows.Children.Add(button);
        }
    }
    private IEnumerable<(DateOnly Date,double Seconds)> CompleteHistoryDays(DateOnly latestDay)
    {
        var totals=engine.Data.Sessions.GroupBy(s=>s.DayKey).ToDictionary(g=>DateOnly.ParseExact(g.Key,"yyyy-MM-dd",CultureInfo.InvariantCulture),g=>g.Sum(s=>s.EffectiveSeconds));
        if(totals.Count==0)yield break;
        var earliest=totals.Keys.Min();
        for(int i=0;i<=latestDay.DayNumber-earliest.DayNumber;i++)
        {
            var date=earliest.AddDays(i);yield return (date,totals.GetValueOrDefault(date));
        }
    }
    private void ShowHistoryStatistics()
    {
        historyMode="statistics";HistoryRows.Children.Clear();PageScroller.ScrollToTop();
        PageScroller.VerticalScrollBarVisibility=ScrollBarVisibility.Disabled;
        var title=new TextBlock {Text=Text("历史统计"),FontSize=13,FontWeight=FontWeights.SemiBold,Height=24};
        HistoryRows.Children.Add(title);
        var day=DateOnly.ParseExact(LogicalDay.Key(DateTimeOffset.Now,engine.Zone,engine.Data.Settings.DayResetMinutes),"yyyy-MM-dd",CultureInfo.InvariantCulture);
        var totals=engine.Data.Sessions.GroupBy(s=>s.DayKey).ToDictionary(g=>DateOnly.ParseExact(g.Key,"yyyy-MM-dd",CultureInfo.InvariantCulture),g=>g.Sum(s=>s.EffectiveSeconds));
        var strip=new HistoryStatisticsStrip(totals,day,engine.Data.Settings.GoalSeconds,ThemeColor,FontFamily);
        liveHistoryStrip=strip;
        AutomationProperties.SetName(strip,Text("历史统计"));
        strip.HoverChanged+=(date,seconds)=>title.Text=date is {} value?value.ToString("yyyy-MM-dd")+"  "+CompactTime(seconds):Text("历史统计");
        var scroll=new ScrollViewer {Height=56,CanContentScroll=true,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=strip};
        scroll.PreviewMouseWheel+=(_,e)=> {scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset-e.Delta/120.0*18);e.Handled=true;};
        HistoryRows.Children.Add(scroll);
        scroll.Loaded+=(_,_)=>scroll.Dispatcher.BeginInvoke(()=>scroll.ScrollToRightEnd(),DispatcherPriority.ContextIdle);
    }
    private void ShowHistoryDetail(string day)
    {
        liveHistoryStrip=null;
        historyMode="detail";detailDay=day;HistoryRows.Children.Clear();PageScroller.ScrollToTop();
        PageScroller.VerticalScrollBarVisibility=ScrollBarVisibility.Visible;
        var title=new Grid {Height=24,Margin=new Thickness(6,0,6,0)};title.ColumnDefinitions.Add(new());title.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        title.Children.Add(new TextBlock {Text=day,FontSize=13,FontWeight=FontWeights.Bold,FontFamily=new FontFamily("Consolas")});
        var hint=new TextBlock {Text=Text("修改后回车保存"),FontSize=12,Foreground=new SolidColorBrush(Color.FromArgb(97,255,255,255))};Grid.SetColumn(hint,1);title.Children.Add(hint);HistoryRows.Children.Add(title);
        foreach(var group in engine.Data.Sessions.Where(s=>s.DayKey==day).GroupBy(s=>s.ApplicationIdentifier,StringComparer.OrdinalIgnoreCase).OrderByDescending(g=>g.Sum(s=>s.EffectiveSeconds)))
        {
            var total=group.Sum(s=>s.EffectiveSeconds);
            Session[] Records()=>engine.Data.Sessions.Where(s=>s.DayKey==day&&string.Equals(s.ApplicationIdentifier,group.Key,StringComparison.OrdinalIgnoreCase)).ToArray();
            var row=new Grid {Height=32,Margin=new Thickness(6,0,6,0)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            row.Children.Add(new TextBlock {Text=group.First().ApplicationName,FontSize=13,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,0,4,0)});
            var input=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
            var hours=new TextBox {Text=DurationText.Hours(total),Width=28,Height=24,Padding=new Thickness(2,0,2,0),FontSize=13};
            var minutes=new TextBox {Text=DurationText.Minutes(total),Width=28,Height=24,Padding=new Thickness(2,0,2,0),FontSize=13};
            AutomationProperties.SetName(hours,group.First().ApplicationName+" · "+Text("小时"));
            AutomationProperties.SetName(minutes,group.First().ApplicationName+" · "+Text("分钟"));
            AutomationProperties.SetAutomationId(hours,"HistoryHours/"+day+"/"+group.Key.ToUpperInvariant());
            AutomationProperties.SetAutomationId(minutes,"HistoryMinutes/"+day+"/"+group.Key.ToUpperInvariant());
            input.Children.Add(hours);input.Children.Add(new TextBlock {Text=engine.Data.Settings.Language=="en"?" h ":" 时 ",FontSize=13,VerticalAlignment=VerticalAlignment.Center});input.Children.Add(minutes);input.Children.Add(new TextBlock {Text=engine.Data.Settings.Language=="en"?" m":" 分",FontSize=13,VerticalAlignment=VerticalAlignment.Center});
            void Restore() {var saved=Records().Sum(s=>s.EffectiveSeconds);hours.Text=DurationText.Hours(saved);minutes.Text=DurationText.Minutes(saved);}
            void Commit(object sender,KeyEventArgs e)
            {
                if(e.Key==Key.Escape){Restore();e.Handled=true;return;}
                if(e.Key!=Key.Enter)return;e.Handled=true;
                if(!TryHistoryDuration(hours.Text,minutes.Text,out var seconds)){Restore();hint.Text=engine.Data.Settings.Language=="en"?"Invalid time":"时间无效";return;}
                // Checkpoint pending work without the ordinary tick's separate
                // save/error navigation. The requested total owns this boundary.
                if(!smoke&&!suspended)RefreshActivityObservation();var records=Records();var previous=records.Select(s=>s.EffectiveSeconds).ToArray();
                for(int i=0;i<records.Length;i++)records[i].EffectiveSeconds=i==0?seconds:0;
                try{store.Save(engine.Data);lastSaved=JsonSerializer.Serialize(engine.Data,AtomicStore.JsonOptions);lastSaveError=null;Restore();hint.Text=Text("修改后回车保存");Render();}
                catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidDataException){for(int i=0;i<records.Length;i++)records[i].EffectiveSeconds=previous[i];RememberSaveError(error);Restore();hint.Text=engine.Data.Settings.Language=="en"?"Save failed":"保存失败";}
            }
            hours.KeyDown+=Commit;minutes.KeyDown+=Commit;
            hours.LostKeyboardFocus+=(_,e)=> {if(!ReferenceEquals(e.NewFocus,minutes))Restore();};
            minutes.LostKeyboardFocus+=(_,e)=> {if(!ReferenceEquals(e.NewFocus,hours))Restore();};
            Grid.SetColumn(input,1);row.Children.Add(input);HistoryRows.Children.Add(row);
        }
        if(HistoryRows.Children.Count==1)HistoryRows.Children.Add(new TextBlock {Text=engine.Data.Settings.Language=="en"?"No application records":"无应用记录",FontSize=12,Margin=new Thickness(6,6,6,0)});
    }
    private static bool TryHistoryDuration(string hours,string minutes,out double seconds)
    {
        seconds=0;hours=hours.Trim();minutes=minutes.Trim();
        if(hours.Length==0||minutes.Length==0||hours.Any(c=>c<'0'||c>'9')||minutes.Any(c=>c<'0'||c>'9')
            ||!long.TryParse(hours,NumberStyles.None,CultureInfo.InvariantCulture,out var h)||!long.TryParse(minutes,NumberStyles.None,CultureInfo.InvariantCulture,out var m))return false;
        try{seconds=checked(h*3600+m*60);return seconds<(double)long.MaxValue;}
        catch(OverflowException){return false;}
    }
}




