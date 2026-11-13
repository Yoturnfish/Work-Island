using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace WorkIsland.App;

public partial class IslandWindow
{
    private readonly SolidColorBrush orbBrush=new(Color.FromRgb(255,149,0));
    private readonly DropShadowEffect orbGlow=new(){Color=Color.FromRgb(255,149,0),BlurRadius=8,ShadowDepth=0,Opacity=.6};
    private ClockGroup? stateTransition;
    private AnimationClock? statusPulseClock;
    private long statusPulseEpoch;
    private string statusPulseState="tracking";
    private bool statusPulseReduced,statusPulseHandoff,statusPulseNative;
    private readonly List<Action<AnimationClock?>> stateBindings=[];
    private int stateTransitionSerial,pulseSerial;
    private string statePresentationKey="",pulseKey="";
    private bool statePresentationInitialized;

    private void StopStateTransition()
    {
        ++stateTransitionSerial;stateTransition?.Controller?.Remove();stateTransition=null;
        foreach(var apply in stateBindings)apply(null);
        stateBindings.Clear();
        foreach(var icon in new FrameworkElement[]{ClockIcon,CoffeeIcon,PauseIcon})
            icon.Visibility=icon.Opacity==0?Visibility.Collapsed:Visibility.Visible;
    }
    private void PresentState(string state,bool reduced)
    {
        var continueNative=false;
        var color=state switch{"tracking"=>ThemeColor,"paused"=>Color.FromRgb(173,173,173),_=>Color.FromRgb(255,149,0)};
        var key=$"{state}/{color}/{reduced}";
        var forced=visualState.Length==0;
        visualState=state;
        if(statePresentationKey!=key || forced)
        {
            continueNative=statePresentationInitialized&&!reduced&&nativeForegroundActive&&NativeStateFrameAllowed;
            if(continueNative){SetNativeStatusPulse(false);GoalProgress.SetNativePulseSuspended(false);}
            else SuspendNativeForeground();
            statePresentationKey=key;
            var animate=statePresentationInitialized&&!reduced;
            statePresentationInitialized=true;
            StatusDot.Fill=orbBrush;StatusDot.Effect=orbGlow;
            if(StateWash.Background is not LinearGradientBrush wash)
            {
                wash=new(){StartPoint=new Point(0,0),EndPoint=new Point(1,1)};
                wash.GradientStops.Add(new(Colors.Transparent,0));wash.GradientStops.Add(new(Colors.Transparent,1));StateWash.Background=wash;
            }
            var targets=new List<(DependencyObject Element,DependencyProperty Property,object Value,object From,Action<AnimationClock?> Apply)>();
            void Track(DependencyObject element,DependencyProperty property,object value,Action<AnimationClock?> apply)
                =>targets.Add((element,property,value,element.GetValue(property),apply));
            foreach(var icon in new FrameworkElement[]{ClockIcon,CoffeeIcon,PauseIcon})
            {
                var selected=state=="tracking"?icon==ClockIcon:state=="paused"?icon==PauseIcon:icon==CoffeeIcon;
                Track(icon,OpacityProperty,selected?1d:0d,clock=>icon.ApplyAnimationClock(OpacityProperty,clock));
            }
            Track(statusBrush,SolidColorBrush.ColorProperty,color,clock=>statusBrush.ApplyAnimationClock(SolidColorBrush.ColorProperty,clock));
            Track(orbBrush,SolidColorBrush.ColorProperty,state=="paused"?Color.FromArgb(31,255,255,255):color,clock=>orbBrush.ApplyAnimationClock(SolidColorBrush.ColorProperty,clock));
            Track(orbGlow,DropShadowEffect.ColorProperty,color,clock=>orbGlow.ApplyAnimationClock(DropShadowEffect.ColorProperty,clock));
            Track(orbGlow,DropShadowEffect.OpacityProperty,state=="paused"?0d:.6,clock=>orbGlow.ApplyAnimationClock(DropShadowEffect.OpacityProperty,clock));
            var washColors=new[]{state=="away"?Color.FromArgb(10,255,149,0):Color.FromArgb(6,255,255,255),state=="away"?Color.FromArgb(51,255,149,0):Color.FromArgb(26,255,255,255)};
            for(var index=0;index<2;index++)
            {
                var stop=wash.GradientStops[index];Track(stop,GradientStop.ColorProperty,washColors[index],clock=>stop.ApplyAnimationClock(GradientStop.ColorProperty,clock));
            }
            Track(StateWash,OpacityProperty,state=="tracking"?0d:1d,clock=>StateWash.ApplyAnimationClock(OpacityProperty,clock));
            // Capture every presented value before removing old clocks. A quick
            // reversal starts here, rather than snapping to the previous endpoint.
            StopStateTransition();
            var timeline=new ParallelTimeline{Duration=TimeSpan.FromSeconds(.35)};
            var easing=new SineEase{EasingMode=EasingMode.EaseInOut};easing.Freeze();
            foreach(var target in targets)
            {
                target.Element.SetValue(target.Property,animate?target.From:target.Value);
                if(!animate)continue;
                AnimationTimeline animation=target.Value is Color toColor
                    ?new ColorAnimation((Color)target.From,toColor,TimeSpan.FromSeconds(.35)){EasingFunction=easing}
                    :new DoubleAnimation((double)target.From,(double)target.Value,TimeSpan.FromSeconds(.35)){EasingFunction=easing};
                timeline.Children.Add(animation);stateBindings.Add(target.Apply);
            }
            if(animate)
            {
                var clock=(ClockGroup)timeline.CreateClock(true);stateTransition=clock;
                for(var index=0;index<stateBindings.Count;index++)stateBindings[index]((AnimationClock)clock.Children[index]);
                foreach(var icon in new FrameworkElement[]{ClockIcon,CoffeeIcon,PauseIcon})
                    if(icon.Opacity>0 || (state=="tracking"?icon==ClockIcon:state=="paused"?icon==PauseIcon:icon==CoffeeIcon))icon.Visibility=Visibility.Visible;
                var serial=stateTransitionSerial;
                clock.Completed+=(_,_)=>
                {
                    if(serial!=stateTransitionSerial)return;
                    foreach(var target in targets)target.Element.SetValue(target.Property,target.Value);
                    StopStateTransition();
                    QueueNativeForeground();
                };clock.Controller!.Begin();
            }
            else StopStateTransition();
        }
        PresentCompactInk(reduced);
        UpdateStatusPulse(state,reduced);
        if(continueNative)BeginNativeStateTransition();
        if(reduced && (rootTransition is not null || pageTransition is not null))SettleNavigation();
    }
    private void UpdateStatusPulse(string state,bool reduced)
    {
        reduced|=!CompactPanel.IsVisible;
        var key=$"{state}/{reduced}";if(pulseKey==key)return;pulseKey=key;
        var serial=++pulseSerial;var scale=(ScaleTransform)StatusIcon.RenderTransform;var from=scale.ScaleX;
        statusPulseState=state;statusPulseReduced=reduced;statusPulseHandoff=false;statusPulseNative=false;
        statusPulseClock?.Controller?.Remove();statusPulseClock=null;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);scale.BeginAnimation(ScaleTransform.ScaleYProperty,null);
        scale.ScaleX=scale.ScaleY=1;
        if(reduced)return;
        void Breathe()
        {
            if(serial!=pulseSerial)return;statusPulseHandoff=false;
            if(state!="tracking"){statusPulseEpoch=System.Diagnostics.Stopwatch.GetTimestamp();StartStatusPulseClock();}
            QueueNativeForeground();
        }
        if(Math.Abs(from-1)<.005){Breathe();return;}
        scale.ScaleX=scale.ScaleY=from;
        statusPulseHandoff=true;
        var handoff=new DoubleAnimation(from,1,TimeSpan.FromSeconds(.2)){EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}};
        var handoffClock=(AnimationClock)handoff.CreateClock(true);statusPulseClock=handoffClock;
        var completed=false;
        handoffClock.Completed+=(_,_)=>
        {
            if(serial!=pulseSerial || completed)return;completed=true;scale.ScaleX=scale.ScaleY=1;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);scale.BeginAnimation(ScaleTransform.ScaleYProperty,null);handoffClock.Controller!.Remove();statusPulseClock=null;Breathe();
        };
        scale.ApplyAnimationClock(ScaleTransform.ScaleXProperty,handoffClock);scale.ApplyAnimationClock(ScaleTransform.ScaleYProperty,handoffClock);
    }
    private double StatusPulsePeriod=>statusPulseState=="paused"?1.8:5.6;
    private double StatusPulseTarget=>statusPulseState=="paused"?.86:1.08;
    private void StartStatusPulseClock()
    {
        if(statusPulseNative||statusPulseReduced||statusPulseHandoff||statusPulseState=="tracking"||PresentationReduced)return;
        var phase=PulsePhase.At(statusPulseEpoch,StatusPulsePeriod);var scale=(ScaleTransform)StatusIcon.RenderTransform;
        scale.ScaleX=scale.ScaleY=PulsePhase.Value(phase,1,StatusPulseTarget);
        var animation=new DoubleAnimation(1,StatusPulseTarget,TimeSpan.FromSeconds(StatusPulsePeriod/2))
            {AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut},BeginTime=-TimeSpan.FromSeconds(phase*StatusPulsePeriod)};
        Timeline.SetDesiredFrameRate(animation,12);
        statusPulseClock=(AnimationClock)animation.CreateClock(true);
        scale.ApplyAnimationClock(ScaleTransform.ScaleXProperty,statusPulseClock);scale.ApplyAnimationClock(ScaleTransform.ScaleYProperty,statusPulseClock);
    }
    private void SetNativeStatusPulse(bool value)
    {
        if(statusPulseNative==value)return;
        if(value&&statusPulseHandoff)throw new InvalidOperationException("Cannot snapshot a live status handoff.");
        statusPulseNative=value;statusPulseClock?.Controller?.Remove();statusPulseClock=null;
        var scale=(ScaleTransform)StatusIcon.RenderTransform;scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);scale.BeginAnimation(ScaleTransform.ScaleYProperty,null);scale.ScaleX=scale.ScaleY=1;
        if(!value)StartStatusPulseClock();
    }
    private void SettleNavigation()
    {
        ++rootTransitionSerial;++transitionSerial;StopTransition(true);StopTransition(false);
        if(OnboardingActive){SettleOnboarding();return;}
        CompactPanel.Visibility=isExpanded?Visibility.Collapsed:Visibility.Visible;
        DetailsPanel.Visibility=isExpanded?Visibility.Visible:Visibility.Collapsed;
        NavigationHome.Visibility=activePage is null || activePage==NavigationHome?Visibility.Visible:Visibility.Collapsed;
        Subpage.Visibility=isExpanded&&activePage is not null&&activePage!=NavigationHome?Visibility.Visible:Visibility.Collapsed;
        foreach(var page in new FrameworkElement[]{SettingsPage,RecognitionPage,HistoryPage})page.Visibility=page==activePage?Visibility.Visible:Visibility.Collapsed;
    }
}
