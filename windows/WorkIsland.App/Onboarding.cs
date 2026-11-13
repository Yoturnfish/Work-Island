using WorkIsland.Core;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace WorkIsland.App;

public partial class IslandWindow
{
    private Grid? onboardingRoot;
    private readonly Grid[] onboardingSteps=new Grid[5];
    private readonly List<Action> onboardingLabels=[];
    private ComboBox? onboardingGoal;
    private Button? onboardingBack,onboardingNext;
    private TextBlock? onboardingHint,onboardingStatus;
    private readonly Border[] onboardingMarks=new Border[5];
    private readonly List<(Button Button,string Language)> onboardingLanguages=[];
    private int onboardingStep=-1,onboardingSerial,onboardingRootSerial;
    private bool refreshingOnboarding;
    private Point? onboardingOrigin;
    private ClockGroup? onboardingMotion,onboardingRootMotion;
    private FrameworkElement? onboardingRootOutgoing;
    private bool OnboardingActive=>onboardingStep>=0;
    private bool OnboardingReduced=>engine.Data.Settings.ReduceMotion||!SystemParameters.ClientAreaAnimation||fullscreenSuppressed||suspended||!IsVisible;
    private string OnboardingText(string zh,string en)=>engine.Data.Settings.Language=="en"?en:zh;

    private void InitializeOnboarding()
    {
        onboardingRoot=new Grid{Width=344,Height=92,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Visibility=Visibility.Collapsed,
            LayoutTransform=new ScaleTransform(engine.Data.Settings.IslandScale,engine.Data.Settings.IslandScale),RenderTransformOrigin=new Point(.5,.5)};
        Panel.SetZIndex(onboardingRoot,10);PageHost.Children.Add(onboardingRoot);
        var row=new Grid{Margin=new Thickness(14,18,14,18)};
        foreach(var width in new[]{new GridLength(22),new GridLength(8),new GridLength(1,GridUnitType.Star),new GridLength(8),new GridLength(22)})row.ColumnDefinitions.Add(new(){Width=width});
        onboardingRoot.Children.Add(row);
        Button Arrow(bool forward)
        {
            var symbol=new MacSymbol{Symbol="backward.end.fill",Width=16,Height=16,IsHitTestVisible=false};
            if(forward)symbol.RenderTransform=new ScaleTransform(-1,1,8,8);
            return new(){Width=22,Height=36,VerticalAlignment=VerticalAlignment.Center,Content=symbol};
        }
        onboardingBack=Arrow(false);onboardingBack.Click+=(_,_)=>MoveOnboarding(-1);row.Children.Add(onboardingBack);
        onboardingNext=Arrow(true);onboardingNext.Click+=(_,_)=>{if(onboardingStep==4)FinishOnboarding();else MoveOnboarding(1);};Grid.SetColumn(onboardingNext,4);row.Children.Add(onboardingNext);
        var host=new Grid{ClipToBounds=true};Grid.SetColumn(host,2);row.Children.Add(host);
        for(var i=0;i<5;i++){onboardingSteps[i]=new(){Visibility=Visibility.Collapsed,RenderTransform=new TranslateTransform()};host.Children.Add(onboardingSteps[i]);}
        var marks=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(40,12,0,0),IsHitTestVisible=false};
        for(var i=0;i<5;i++){onboardingMarks[i]=new(){Width=9,Height=3,CornerRadius=new CornerRadius(1),Margin=new Thickness(0,0,3,0)};marks.Children.Add(onboardingMarks[i]);}onboardingRoot.Children.Add(marks);
        TextBlock Label(string zh,string en,double size=12,FontWeight? weight=null)
        {
            var label=new TextBlock{FontSize=size,FontWeight=weight??FontWeights.Normal,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};
            onboardingLabels.Add(()=>label.Text=OnboardingText(zh,en));return label;
        }
        var languages=new Grid{Margin=new Thickness(8,0,8,0),VerticalAlignment=VerticalAlignment.Center};
        foreach(var width in new[]{new GridLength(21),new GridLength(12),new GridLength(1,GridUnitType.Star),new GridLength(12),new GridLength(1,GridUnitType.Star)})languages.ColumnDefinitions.Add(new(){Width=width});
        languages.Children.Add(new OnboardingArtwork{Kind="globe",Width=21,Height=21});
        foreach(var (code,title,column) in new[]{("en","English",2),("zh","中文",4)})
        {
            var button=new Button{Content=title,Height=32,FontSize=15,FontWeight=FontWeights.SemiBold};Grid.SetColumn(button,column);languages.Children.Add(button);onboardingLanguages.Add((button,code));
            button.Click+=(_,_)=>{engine.Data.Settings.Language=code;Persist();ApplyLanguage();SyncOnboardingSettings();Render();};
        }
        onboardingSteps[0].Children.Add(languages);
        var goal=new Grid{VerticalAlignment=VerticalAlignment.Center};goal.ColumnDefinitions.Add(new());goal.ColumnDefinitions.Add(new(){Width=new GridLength(70)});
        var goalTitle=Label("设定每日工作目标","Daily work goal",16,FontWeights.SemiBold);goalTitle.Margin=new Thickness(0,0,6,0);goal.Children.Add(goalTitle);
        onboardingGoal=new(){Width=70,Height=24,Foreground=new SolidColorBrush(ThemeColor)};Grid.SetColumn(onboardingGoal,1);goal.Children.Add(onboardingGoal);
        foreach(var hours in new[]{1,2,3,4,6,8})onboardingGoal.Items.Add(new ComboBoxItem{Tag=(double)hours*3600});
        onboardingGoal.SelectionChanged+=(_,_)=>
        {
            if(refreshingOnboarding||onboardingGoal.SelectedItem is not ComboBoxItem{Tag:double seconds})return;
            engine.Data.Settings.GoalSeconds=seconds;GoalInput.Text=(seconds/3600).ToString(CultureInfo.InvariantCulture);Persist();SyncOnboardingSettings();Render();
        };
        onboardingSteps[1].Children.Add(goal);
        var auto=new Button{Height=28,Background=new SolidColorBrush(Color.FromArgb(20,255,255,255)),FontSize=12,VerticalAlignment=VerticalAlignment.Center};
        var autoContent=new StackPanel{Orientation=Orientation.Horizontal};autoContent.Children.Add(new OnboardingArtwork{Kind="recognition",Width=16,Height=16,Margin=new Thickness(0,0,5,0)});autoContent.Children.Add(Label("自动识别","Auto-detect",12,FontWeights.Medium));auto.Content=autoContent;
        AutomationProperties.SetAutomationId(auto,"OnboardingAutoDetect");auto.Click+=async(_,_)=>await OpenRecognitionPicker(auto,"all");onboardingSteps[2].Children.Add(auto);
        onboardingHint=Label("你也可以稍后手动添加","You can also add items manually later",9);onboardingHint.HorizontalAlignment=HorizontalAlignment.Center;onboardingHint.VerticalAlignment=VerticalAlignment.Bottom;onboardingHint.Margin=new Thickness(0,0,0,0);onboardingHint.Foreground=new SolidColorBrush(Color.FromArgb(173,255,255,255));onboardingSteps[2].Children.Add(onboardingHint);
        var tutorial=new Grid{VerticalAlignment=VerticalAlignment.Center};tutorial.ColumnDefinitions.Add(new());tutorial.ColumnDefinitions.Add(new(){Width=new GridLength(21)});tutorial.ColumnDefinitions.Add(new());
        var divider=new Border{Width=1,Background=new SolidColorBrush(Color.FromArgb(38,255,255,255))};Grid.SetColumn(divider,1);tutorial.Children.Add(divider);
        foreach(var (kind,column,zh,en) in new[]{("drag",0,"窗口可拖动","Drag the window"),("menu",2,"点圆环进菜单","Tap ring for menu")})
        {
            var diagram=new StackPanel();diagram.Children.Add(new OnboardingArtwork{Kind=kind,Height=36});var caption=Label(zh,en,10,FontWeights.Medium);caption.HorizontalAlignment=HorizontalAlignment.Center;caption.Margin=new Thickness(0,2,0,0);diagram.Children.Add(caption);Grid.SetColumn(diagram,column);tutorial.Children.Add(diagram);
        }
        AutomationProperties.SetName(tutorial,"Drag outside the circle to move the window; click the circle to open navigation.");onboardingSteps[3].Children.Add(tutorial);
        var complete=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};complete.Children.Add(new OnboardingArtwork{Kind="complete",Width=26,Height=26,Margin=new Thickness(0,0,10,0)});complete.Children.Add(Label("开始你的工作","Ready to work",17,FontWeights.SemiBold));onboardingSteps[4].Children.Add(complete);
        onboardingStatus=new(){FontSize=9,Foreground=new SolidColorBrush(Color.FromRgb(255,183,85)),TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(40,0,40,3),VerticalAlignment=VerticalAlignment.Bottom,Visibility=Visibility.Collapsed};onboardingRoot.Children.Add(onboardingStatus);
        RefreshOnboardingLabels();
    }
    private void SyncOnboardingSettings()
    {
        var previous=loadingPreferences;loadingPreferences=true;
        try
        {
            LanguagePicker.SelectedItem=LanguagePicker.Items.Cast<ComboBoxItem>().First(i=>i.Tag?.ToString()==engine.Data.Settings.Language);
            GoalPicker.SelectedItem=GoalPicker.Items.Cast<ComboBoxItem>().FirstOrDefault(i=>i.Tag?.ToString()==(engine.Data.Settings.GoalSeconds/3600).ToString(CultureInfo.InvariantCulture));
        }
        finally{loadingPreferences=previous;}
        RefreshOnboardingLabels();
    }
    private void RefreshOnboardingLabels()
    {
        if(onboardingRoot is null)return;
        foreach(var update in onboardingLabels)update();
        refreshingOnboarding=true;
        try
        {
            if(onboardingGoal is not null)
            {
                if(!onboardingGoal.Items.Cast<ComboBoxItem>().Any(i=>Equals(i.Tag,engine.Data.Settings.GoalSeconds)))onboardingGoal.Items.Add(new ComboBoxItem{Tag=engine.Data.Settings.GoalSeconds});
                foreach(var item in onboardingGoal.Items.Cast<ComboBoxItem>())item.Content=((double)item.Tag/3600).ToString("0.##",CultureInfo.InvariantCulture)+OnboardingText(" 小时"," h");
                onboardingGoal.SelectedItem=onboardingGoal.Items.Cast<ComboBoxItem>().First(i=>Equals(i.Tag,engine.Data.Settings.GoalSeconds));
            }
        }
        finally{refreshingOnboarding=false;}
        foreach(var (button,language) in onboardingLanguages){button.Background=new SolidColorBrush(Color.FromArgb(engine.Data.Settings.Language==language?(byte)56:(byte)20,255,255,255));AutomationProperties.SetItemStatus(button,engine.Data.Settings.Language==language?OnboardingText("已选择","Selected"):"");}
        if(onboardingBack is null||onboardingNext is null)return;
        onboardingBack.IsEnabled=onboardingStep>0;onboardingBack.Opacity=onboardingStep>0?1:0;
        AutomationProperties.SetName(onboardingBack,OnboardingText("上一步","Back"));AutomationProperties.SetName(onboardingNext,onboardingStep==4?OnboardingText("完成","Done"):OnboardingText("下一步","Next"));
        if(onboardingNext.Content is MacSymbol next)next.Visibility=onboardingStep==4?Visibility.Collapsed:Visibility.Visible;
        if(onboardingStep==4)onboardingNext.Content=new OnboardingArtwork{Kind="check",Width=16,Height=16};
        else if(onboardingNext.Content is not MacSymbol)onboardingNext.Content=new MacSymbol{Symbol="backward.end.fill",Width=16,Height=16,RenderTransform=new ScaleTransform(-1,1,8,8),IsHitTestVisible=false};
        for(var i=0;i<5;i++)onboardingMarks[i].Background=new SolidColorBrush(Color.FromArgb(i<=onboardingStep?(byte)255:(byte)56,64,255,97));
        AutomationProperties.SetName(onboardingRoot,OnboardingText("设置引导","Setup")+$" {Math.Max(0,onboardingStep)+1}/5");
    }
    private void ReopenOnboarding(object sender,MouseButtonEventArgs args){if(args.ClickCount!=3)return;args.Handled=true;BeginOnboarding();}
    internal void BeginOnboarding()
    {
        if(applicationChooserOpen)return;
        CloseRecognitionPicker();focusReturn.Stop();CancelTitleEdit(null,null!);StopTransition(true);StopTransition(false);
        StopOnboardingMotion();StopOnboardingRootMotion();onboardingOrigin??=new Point(Left,Top);onboardingStep=0;isExpanded=false;
        ApplyDisplayMode();SettleOnboarding();RefreshOnboardingLabels();ApplyMaterial();Render();
        if(!fullscreenSuppressed&&!suspended){Show();ClampToScreen();if(!smoke)Activate();}
        StartOnboardingRootMotion(true);
    }
    internal void MoveOnboarding(int direction)
    {
        if(!OnboardingActive)return;
        var next=RuntimeCompat.Clamp(onboardingStep+direction,0,4);if(next==onboardingStep)return;
        CloseRecognitionPicker();if(onboardingGoal is not null)onboardingGoal.IsDropDownOpen=false;OnboardingMessage("");
        var outgoing=onboardingSteps[onboardingStep];var incoming=onboardingSteps[next];
        var outgoingMove=(TranslateTransform)outgoing.RenderTransform;var incomingMove=(TranslateTransform)incoming.RenderTransform;
        var fromX=outgoingMove.X;var fromAlpha=outgoing.Opacity;var returning=incomingMove.HasAnimatedProperties;var toX=returning?incomingMove.X:Math.Sign(direction)*256;var toAlpha=returning?incoming.Opacity:0;
        StopOnboardingMotion();onboardingStep=next;SettleOnboardingSteps();RefreshOnboardingLabels();
        if(OnboardingReduced)return;
        incomingMove.X=toX;incoming.Opacity=toAlpha;outgoingMove.X=fromX;outgoing.Opacity=fromAlpha;
        outgoing.Visibility=incoming.Visibility=Visibility.Visible;var timeline=new ParallelTimeline{Duration=TimeSpan.FromSeconds(.24)};
        foreach(var (from,to) in new[]{(toX,0d),(fromX,-Math.Sign(direction)*256d),(toAlpha,1d),(fromAlpha,0d)})timeline.Children.Add(new DoubleAnimation(from,to,TimeSpan.FromSeconds(.24)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseInOut}});
        var clock=(ClockGroup)timeline.CreateClock(true);onboardingMotion=clock;var serial=++onboardingSerial;
        incomingMove.ApplyAnimationClock(TranslateTransform.XProperty,(AnimationClock)clock.Children[0]);outgoingMove.ApplyAnimationClock(TranslateTransform.XProperty,(AnimationClock)clock.Children[1]);incoming.ApplyAnimationClock(OpacityProperty,(AnimationClock)clock.Children[2]);outgoing.ApplyAnimationClock(OpacityProperty,(AnimationClock)clock.Children[3]);
        clock.Completed+=(_,_)=>{if(serial==onboardingSerial){StopOnboardingMotion();SettleOnboardingSteps();}};clock.Controller!.Begin();
    }
    private void StopOnboardingMotion()
    {
        ++onboardingSerial;onboardingMotion?.Controller?.Remove();onboardingMotion=null;
        foreach(var step in onboardingSteps.Where(step=>step is not null)){var movement=(TranslateTransform)step.RenderTransform;movement.BeginAnimation(TranslateTransform.XProperty,null);movement.X=0;step.BeginAnimation(OpacityProperty,null);step.Opacity=1;}
    }
    private void SettleOnboardingSteps(){for(var i=0;i<5;i++)onboardingSteps[i].Visibility=i==onboardingStep?Visibility.Visible:Visibility.Collapsed;}
    private void SettleOnboarding()
    {
        StopOnboardingMotion();StopOnboardingRootMotion();CompactPanel.Visibility=DetailsPanel.Visibility=Visibility.Collapsed;
        if(onboardingRoot is not null){onboardingRoot.Visibility=Visibility.Visible;onboardingRoot.Opacity=1;}SettleOnboardingSteps();
    }
    private void SettleOnboardingIfReduced()
    {
        if(!OnboardingReduced)return;
        if(materialMotion is not null)StopMaterialMotion(true);
        if(OnboardingActive && (onboardingMotion is not null || onboardingRootMotion is not null))SettleOnboarding();
        else if(!OnboardingActive && onboardingRootMotion is not null){StopOnboardingRootMotion();if(onboardingRoot is not null)onboardingRoot.Visibility=Visibility.Collapsed;}
    }
    internal void FinishOnboarding()
    {
        if(!OnboardingActive||onboardingStep!=4)return;
        var previouslyCompleted=engine.Data.Settings.HasCompletedOnboarding;engine.Data.Settings.HasCompletedOnboarding=true;
        if(!SaveState(true)){engine.Data.Settings.HasCompletedOnboarding=previouslyCompleted;return;}
        holdOnboardingOpacity=true;
        CloseRecognitionPicker();StopOnboardingMotion();SettleOnboardingSteps();StopOnboardingRootMotion();onboardingStep=-1;isExpanded=false;
        if(onboardingOrigin is {} origin){Left=origin.X;Top=origin.Y;ClampToScreen();}onboardingOrigin=null;
        CompactPanel.Visibility=Visibility.Visible;DetailsPanel.Visibility=Visibility.Collapsed;ApplyMaterial();Render();
        if(engine.Data.Settings.DisplayMode=="menuBar"){onboardingRoot!.Visibility=Visibility.Collapsed;ApplyDisplayMode();return;}
        StartOnboardingRootMotion(false);
    }
    private void StartOnboardingRootMotion(bool entering)
    {
        if(onboardingRoot is null)return;
        if(OnboardingReduced){onboardingRoot.Visibility=entering?Visibility.Visible:Visibility.Collapsed;return;}
        onboardingRoot.Visibility=Visibility.Visible;onboardingRootOutgoing=entering?null:CompactPanel;
        onboardingRoot.Opacity=entering?0:1;
        var timeline=new ParallelTimeline{Duration=TimeSpan.FromSeconds(.38)};
        timeline.Children.Add(new DoubleAnimation(entering?0:1,entering?1:0,TimeSpan.FromSeconds(.38)){EasingFunction=new QuadraticEase{EasingMode=EasingMode.EaseOut}});
        var scale=new ScaleTransform(1,1);onboardingRoot.RenderTransform=scale;
        for(var i=0;i<2;i++)timeline.Children.Add(new DoubleAnimation(1,entering?1:1.12,TimeSpan.FromSeconds(.38)){EasingFunction=new QuadraticEase{EasingMode=EasingMode.EaseOut}});
        if(!entering){timeline.Children.Add(new DoubleAnimation(0,1,TimeSpan.FromSeconds(.38)));timeline.Children.Add(new DoubleAnimation(.82,1,TimeSpan.FromSeconds(.38)));timeline.Children.Add(new DoubleAnimation(.82,1,TimeSpan.FromSeconds(.38)));CompactPanel.Opacity=0;CompactPanel.RenderTransformOrigin=new Point(.5,.5);CompactPanel.RenderTransform=new ScaleTransform(.82,.82);}
        var clock=(ClockGroup)timeline.CreateClock(true);onboardingRootMotion=clock;var serial=++onboardingRootSerial;
        onboardingRoot.ApplyAnimationClock(OpacityProperty,(AnimationClock)clock.Children[0]);scale.ApplyAnimationClock(ScaleTransform.ScaleXProperty,(AnimationClock)clock.Children[1]);scale.ApplyAnimationClock(ScaleTransform.ScaleYProperty,(AnimationClock)clock.Children[2]);
        if(!entering){CompactPanel.ApplyAnimationClock(OpacityProperty,(AnimationClock)clock.Children[3]);((ScaleTransform)CompactPanel.RenderTransform).ApplyAnimationClock(ScaleTransform.ScaleXProperty,(AnimationClock)clock.Children[4]);((ScaleTransform)CompactPanel.RenderTransform).ApplyAnimationClock(ScaleTransform.ScaleYProperty,(AnimationClock)clock.Children[5]);}
        clock.Completed+=(_,_)=>{if(serial==onboardingRootSerial){StopOnboardingRootMotion();onboardingRoot.Visibility=entering?Visibility.Visible:Visibility.Collapsed;QueueNativeForeground();}};clock.Controller!.Begin();
    }
    private void StopOnboardingRootMotion()
    {
        ++onboardingRootSerial;onboardingRootMotion?.Controller?.Remove();onboardingRootMotion=null;
        if(onboardingRoot is not null){onboardingRoot.BeginAnimation(OpacityProperty,null);onboardingRoot.RenderTransform=Transform.Identity;onboardingRoot.Opacity=1;}
        if(onboardingRootOutgoing is {} outgoing){outgoing.BeginAnimation(OpacityProperty,null);outgoing.RenderTransform=Transform.Identity;outgoing.Opacity=1;onboardingRootOutgoing=null;}
    }
    private void OnboardingMessage(string text)
    {
        if(onboardingStatus is null)return;onboardingStatus.Text=text;onboardingStatus.Visibility=string.IsNullOrEmpty(text)?Visibility.Collapsed:Visibility.Visible;
        if(onboardingHint is not null)onboardingHint.Visibility=string.IsNullOrEmpty(text)?Visibility.Visible:Visibility.Collapsed;
        AutomationProperties.SetHelpText(onboardingStatus,text);
    }
}

internal sealed class OnboardingArtwork:FrameworkElement
{
    public string Kind{get;init;}="globe";
    protected override void OnRender(DrawingContext drawing)
    {
        var white=new Pen(Brushes.White,1.6){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};var green=new SolidColorBrush(Color.FromRgb(64,255,97));
        if(Kind is "globe" or "recognition" or "check" or "complete")
        {
            drawing.PushTransform(new ScaleTransform(ActualWidth/22,ActualHeight/22));
            if(Kind=="globe"){drawing.DrawEllipse(null,white,new Point(11,11),9,9);drawing.DrawEllipse(null,white,new Point(11,11),4,9);drawing.DrawLine(white,new Point(2,11),new Point(20,11));}
            else if(Kind=="recognition"){drawing.DrawGeometry(null,white,Geometry.Parse("M 2,7 L 2,2 L 7,2 M 15,2 L 20,2 L 20,7 M 20,15 L 20,20 L 15,20 M 7,20 L 2,20 L 2,15 M 6,11 L 16,11 M 11,6 L 11,16"));}
            else{if(Kind=="complete")drawing.DrawEllipse(green,null,new Point(11,11),10,10);drawing.DrawGeometry(null,Kind=="complete"?new Pen(Brushes.Black,2.1):white,Geometry.Parse("M 5,11 L 9,15 L 17,7"));}
            drawing.Pop();return;
        }
        drawing.PushTransform(new ScaleTransform(ActualWidth/118,ActualHeight/36));drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(153,0,0,0)),new Pen(new SolidColorBrush(Color.FromArgb(41,255,255,255)),1),new Rect(0,0,118,36),8,8);
        drawing.DrawEllipse(green,null,new Point(16.5,18),8.5,8.5);if(Kind=="menu")drawing.DrawEllipse(null,new Pen(Brushes.White,1),new Point(16.5,18),4.5,4.5);
        drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(128,255,255,255)),null,new Rect(32,12,32,3),1.5,1.5);drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(128,64,255,97)),null,new Rect(32,18,44,4),2,2);
        drawing.PushTransform(new TranslateTransform(Kind=="drag"?59:19,16));drawing.DrawGeometry(Kind=="menu"?Brushes.White:null,Kind=="drag"?white:null,Geometry.Parse(Kind=="drag"?"M -5,5 L 13,5 M -5,5 L -1,1 M -5,5 L -1,9 M 13,5 L 9,1 M 13,5 L 9,9":"M 0,0 L 0,14 L 4,10 L 7,16 L 10,14 L 7,8 L 13,8 Z"));drawing.Pop();drawing.Pop();
    }
}
