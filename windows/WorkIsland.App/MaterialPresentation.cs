using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WorkIsland.App;

public partial class IslandWindow
{
    internal void RefreshSystemMaterial()
    {
        if(exiting)return;
        SuspendNativeForeground();ApplyMaterial();Render();
    }
    private static readonly DependencyProperty MaterialGlassAmountProperty=DependencyProperty.Register(nameof(MaterialGlassAmount),typeof(double),typeof(IslandWindow),new PropertyMetadata(0d,MaterialGlassChanged));
    private double MaterialGlassAmount=>(double)GetValue(MaterialGlassAmountProperty);
    internal bool SuppressMaterialSurfaces=>fullscreenSuppressed||exiting;
    private readonly LinearGradientBrush materialBackground=new(){StartPoint=new Point(0,0),EndPoint=new Point(1,1),GradientStops=new(){new(Colors.Transparent,0),new(Colors.Transparent,1)}};
    private readonly SolidColorBrush materialOutline=new(Color.FromArgb(26,255,255,255));
    private readonly List<(DependencyObject Element,DependencyProperty Property,object Target)> materialBindings=[];
    private ClockGroup? materialMotion;
    private bool materialInitialized,materialOnboarding,changingMaterial,holdOnboardingOpacity;
    private string materialSignature="";
    private int materialSerial;
    private static void MaterialGlassChanged(DependencyObject value,DependencyPropertyChangedEventArgs args)
    {
        var window=(IslandWindow)value;if(window.changingMaterial||window.exiting)return;
        window.surfaces?.UpdateMaterialGlass((double)args.NewValue);
    }
    private void ReleaseOnboardingOpacityOutside(Point point)
    {
        if(!OnboardingActive && (point.X<0||point.Y<0||point.X>=ActualWidth||point.Y>=ActualHeight))holdOnboardingOpacity=false;
    }
    private void UpdateMaterialPresentation()
    {
        var onboarding=OnboardingActive;var glass=engine.Data.Settings.BackgroundStyle=="glass"&&!onboarding;var native=surfaces?.NativeBackdropAvailable==true;
        var signature=$"{onboarding}/{glass}/{native}/{isExpanded}";
        if(materialInitialized && signature==materialSignature)
        {
            if(materialMotion is not null && OnboardingReduced)StopMaterialMotion(true);
            return;
        }
        var animate=materialInitialized&&materialOnboarding!=onboarding&&!OnboardingReduced&&!IsMenuBar;
        if(!nativeStateTransitionActive)SuspendNativeForeground();
        materialSignature=signature;materialOnboarding=onboarding;materialInitialized=true;
        OuterBorder.Background=materialBackground;Outline.BorderBrush=materialOutline;
        var onboardingColor=Color.FromRgb(6,15,10);
        var first=onboarding?onboardingColor:glass?(native?Color.FromArgb(1,0,0,0):Color.FromRgb(31,31,31)):Color.FromArgb(245,0,0,0);
        var second=onboarding?onboardingColor:glass?first:Color.FromArgb(245,6,14,10);
        var targets=new List<(DependencyObject Element,DependencyProperty Property,object Target)>
        {
            (materialBackground.GradientStops[0],GradientStop.ColorProperty,first),(materialBackground.GradientStops[1],GradientStop.ColorProperty,second),
            (this,MaterialGlassAmountProperty,glass?1d:0d),(StateWashMaterialMask,OpacityProperty,glass||onboarding?0d:1d),
            (materialOutline,SolidColorBrush.ColorProperty,Color.FromArgb(glass&&!native?(byte)55:(byte)26,255,255,255)),
            (Outline,OpacityProperty,glass&&native?0d:1d),(GlassEdge,OpacityProperty,glass&&native?1d:0d),
            (GlassPageWash,OpacityProperty,glass&&isExpanded?1d:0d)
        };
        var presented=targets.Select(item=>item.Element.GetValue(item.Property)).ToArray();
        StopMaterialMotion(false);materialBindings.AddRange(targets);
        changingMaterial=true;
        for(var i=0;i<targets.Count;i++){var item=targets[i];item.Element.SetValue(item.Property,animate?presented[i]:item.Target);}
        changingMaterial=false;
        if(!animate){CompleteMaterialPresentation();return;}
        Outline.Visibility=GlassEdge.Visibility=GlassPageWash.Visibility=Visibility.Visible;
        var timeline=new ParallelTimeline{Duration=TimeSpan.FromSeconds(.38)};
        for(var i=0;i<targets.Count;i++)
        {
            var ease=new QuadraticEase{EasingMode=EasingMode.EaseOut};
            if(targets[i].Target is Color color)timeline.Children.Add(new ColorAnimation((Color)presented[i],color,TimeSpan.FromSeconds(.38)){EasingFunction=ease});
            else timeline.Children.Add(new DoubleAnimation((double)presented[i],(double)targets[i].Target,TimeSpan.FromSeconds(.38)){EasingFunction=ease});
        }
        var clock=(ClockGroup)timeline.CreateClock(true);materialMotion=clock;var serial=++materialSerial;
        for(var i=0;i<targets.Count;i++)ApplyMaterialClock(targets[i].Element,targets[i].Property,(AnimationClock)clock.Children[i]);
        clock.Completed+=(_,_)=>{if(serial==materialSerial)StopMaterialMotion(true);};clock.Controller!.Begin();
    }
    private static void ApplyMaterialClock(DependencyObject element,DependencyProperty property,AnimationClock? clock)
    {
        if(element is System.Windows.Media.Animation.Animatable animated)animated.ApplyAnimationClock(property,clock);
        else if(element is UIElement visual)visual.ApplyAnimationClock(property,clock);
    }
    private void StopMaterialMotion(bool settle)
    {
        ++materialSerial;changingMaterial=true;materialMotion?.Controller?.Remove();materialMotion=null;
        foreach(var item in materialBindings){ApplyMaterialClock(item.Element,item.Property,null);if(settle)item.Element.SetValue(item.Property,item.Target);}
        materialBindings.Clear();changingMaterial=false;
        if(settle&&!exiting)CompleteMaterialPresentation();
    }
    private void CompleteMaterialPresentation()
    {
        if(rootTransition is {Children.Count:>4}&&engine.Data.Settings.BackgroundStyle=="glass")
            GlassPageWash.ApplyAnimationClock(OpacityProperty,(AnimationClock)rootTransition.Children[4]);
        Outline.Visibility=Outline.Opacity>0?Visibility.Visible:Visibility.Collapsed;
        GlassEdge.Visibility=GlassEdge.Opacity>0?Visibility.Visible:Visibility.Collapsed;
        GlassPageWash.Visibility=GlassPageWash.Opacity>0||rootTransition is not null&&engine.Data.Settings.BackgroundStyle=="glass"?Visibility.Visible:Visibility.Collapsed;
        surfaces?.UpdateMaterialGlass(MaterialGlassAmount);
        QueueNativeForeground();
    }
}
