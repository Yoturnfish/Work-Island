using System.Drawing;

namespace WorkIsland.App;

public readonly record struct StatusGlyphBlend(double Clock,double Coffee,double Pause);

// A Shell icon has no compositor layer. Sample the same Mac timing with a
// bounded dispatcher timer, independently of the hidden island's WPF clock.
public sealed class StatusItemMotion
{
    public const double TransitionSeconds=.35, CycleSeconds=2.8, FrameRate=24;
    private Color from,target;
    private double changedAt,modeStartedAt,pulseOrigin;
    private bool initialized;
    private StatusGlyphBlend glyphFrom,glyphTarget;
    private string glyphState="";
    private double glyphChangedAt,scanStartedAt;
    public string Mode { get; private set; }="static";
    public bool IsTransitioning(double now)=>initialized && from!=target && now-changedAt<TransitionSeconds;
    public bool IsGlyphTransitioning(double now)=>glyphFrom!=glyphTarget&&now-glyphChangedAt<TransitionSeconds;
    public bool NeedsFrames(double now)=>Mode!="static" || IsTransitioning(now)||IsGlyphTransitioning(now);
    public double ScanPhase(double now)=>(Math.Max(0,now-scanStartedAt)%CycleSeconds)/CycleSeconds;
    public double CoffeePulse(double now)
    {
        if(Mode!="pulse")return 0;
        var elapsed=Math.Max(0,now-pulseOrigin);var fade=Math.Clamp(elapsed/.8,0,1);
        return (1-Math.Cos(elapsed/(2*CycleSeconds)*2*Math.PI))/2*fade*fade*(3-2*fade);
    }
    public StatusGlyphBlend GlyphAt(double now)
    {
        var t=Math.Clamp((now-glyphChangedAt)/TransitionSeconds,0,1);
        var ease=t<.5?4*t*t*t:1-Math.Pow(-2*t+2,3)/2;
        return new(glyphFrom.Clock+(glyphTarget.Clock-glyphFrom.Clock)*ease,
            glyphFrom.Coffee+(glyphTarget.Coffee-glyphFrom.Coffee)*ease,
            glyphFrom.Pause+(glyphTarget.Pause-glyphFrom.Pause)*ease);
    }
    public Color ColorAt(double now)
    {
        var t=Math.Clamp((now-changedAt)/TransitionSeconds,0,1);
        var ease=t<.5?4*t*t*t:1-Math.Pow(-2*t+2,3)/2;
        byte Mix(byte a,byte b)=>(byte)Math.Clamp(Math.Round(a+(b-a)*ease),0,255);
        return Color.FromArgb(Mix(from.A,target.A),Mix(from.R,target.R),Mix(from.G,target.G),Mix(from.B,target.B));
    }
    public void Retarget(Color color,string state,double progress,bool reduced,double now,double? awayElapsed)
    {
        var targetGlyph=state=="tracking"?new StatusGlyphBlend(1,0,0):state=="paused"?new(0,0,1):new StatusGlyphBlend(0,1,0);
        if(!initialized||reduced){glyphFrom=glyphTarget=targetGlyph;glyphChangedAt=now-TransitionSeconds;}
        else if(glyphState!=state){glyphFrom=GlyphAt(now);glyphTarget=targetGlyph;glyphChangedAt=now;}
        glyphState=state;
        if(!initialized || reduced){from=target=color;changedAt=now-TransitionSeconds;initialized=true;}
        else if(target!=color){from=ColorAt(now);target=color;changedAt=now;}
        var next=reduced?"static":state=="tracking"?"scan":state=="away"&&progress<1&&awayElapsed is >=0?"pulse":"static";
        if(next!=Mode){Mode=next;modeStartedAt=now;if(next=="scan")scanStartedAt=now;}
        if(awayElapsed is {} elapsed)pulseOrigin=now-elapsed;
    }
    public double CellOpacity(int column,int lit,double now)
    {
        var filled=column<lit;var opacity=filled?.72:.10;
        if(Mode=="pulse"&&!filled)
        {
            var elapsed=Math.Max(0,now-pulseOrigin);var fade=Math.Clamp(elapsed/.8,0,1);
            var envelope=fade*fade*(3-2*fade);
            opacity+=(1-Math.Cos(elapsed/CycleSeconds*2*Math.PI))/2*envelope*.25;
        }
        if(Mode=="scan")
        {
            var phase=(Math.Max(0,now-modeStartedAt)%CycleSeconds)/CycleSeconds;
            var center=-7+phase*42; // Mac: max(14, width * .26), width=28.
            opacity+=Math.Max(0,1-Math.Abs((column*2.8+.9-center)/7))*.30;
        }
        return Math.Clamp(opacity,0,1);
    }
}
