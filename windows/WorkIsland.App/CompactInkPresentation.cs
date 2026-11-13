using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WorkIsland.App;

public partial class IslandWindow
{
    private AnimationClock? compactInkMotion;
    private Color compactTitleTarget;
    private bool compactInkInitialized;
    private int compactInkSerial;
    private void PresentCompactInk(bool reduced)
    {
        var title=textContrast.Title;
        if(compactInkInitialized&&title==compactTitleTarget)
        {
            if(reduced&&compactInkMotion is not null)StopCompactInk(true);
            return;
        }
        var animate=compactInkInitialized&&!reduced;
        var titleFrom=compactTitleInk.Color;
        StopCompactInk(false);compactInkInitialized=true;
        compactTitleTarget=title;
        compactTitleInk.Color=animate?titleFrom:title;
        if(!animate)return;
        SuspendNativeForeground();
        var ease=new SineEase{EasingMode=EasingMode.EaseInOut};ease.Freeze();
        var clock=(AnimationClock)new ColorAnimation(titleFrom,title,TimeSpan.FromSeconds(.35)){EasingFunction=ease}.CreateClock(true);compactInkMotion=clock;var serial=compactInkSerial;
        compactTitleInk.ApplyAnimationClock(SolidColorBrush.ColorProperty,clock);
        clock.Completed+=(_,_)=>{if(serial!=compactInkSerial)return;StopCompactInk(true);QueueNativeForeground();};
        clock.Controller!.Begin();
    }
    private void StopCompactInk(bool settle)
    {
        ++compactInkSerial;compactInkMotion?.Controller?.Remove();compactInkMotion=null;
        compactTitleInk.ApplyAnimationClock(SolidColorBrush.ColorProperty,null);
        if(settle&&compactInkInitialized)compactTitleInk.Color=compactTitleTarget;
    }
}
