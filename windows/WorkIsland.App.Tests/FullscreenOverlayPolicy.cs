using System;
using System.Reflection;
using WorkIsland.App;

internal static partial class Program
{
    private static void RunFullscreenOverlayPolicy(Action<string,bool,object?> check)
    {
        const BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic;
        bool Passive(long style)=>(bool)typeof(WindowsFullscreenSource).GetMethod("IsPassiveOverlay",flags)!.Invoke(null,[style])!;
        bool Suppress(int? state,bool full,bool overlay)=>(bool)typeof(WindowsFullscreenSource).GetMethod("ShouldSuppress",flags)!.Invoke(null,[state,full,overlay])!;
        check("passive overlay requires layered, transparent and noactivate together",Passive(0x08080020)&&Passive(0x080800a8),null);
        foreach(var style in new long[]{0,0x20,0x80000,0x08000000,0x00080020,0x08080000,0x08000020,0x80,0x8})
            check($"style 0x{style:X} alone does not exempt a fullscreen application",!Passive(style),null);
        foreach(var state in new int?[]{null,1,2,3,4,5,6,7})
        {
            check($"fullscreen application wins over overlay and shell state {state}",Suppress(state,true,true),null);
            check($"passive overlay preserves exclusive/presentation state {state}",Suppress(state,false,true)==(state is 3 or 4),null);
        }
        check("ambiguous BUSY without passive fullscreen evidence remains conservative",Suppress(2,false,false),null);
        check("failed shell query without covering window does not suppress",!Suppress(null,false,false),null);
    }
}
