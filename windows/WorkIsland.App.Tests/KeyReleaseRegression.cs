using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunKeyReleaseChecks(IslandWindow window,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        void Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,args);
        var engine=Field<TrackingEngine>("engine");engine.Data.Settings.Paused=true;engine.Data.Settings.ReduceMotion=false;engine.Data.Settings.Rules=[];
        engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new(null,null,0));Call("Render");await Task.Delay(450);
        Call("EnableNativeForeground");Call("EnableNativePulses");await Task.Delay(150);
        var keys=Field<HashSet<Key>>("nativeForegroundKeys");var source=(HwndSource)PresentationSource.FromVisual(window);
        bool Active()=>Field<bool>("nativeForegroundActive");
        void Down(Key key){window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,0,key){RoutedEvent=Keyboard.PreviewKeyDownEvent});}
        bool Message(int code,int key,long flags=0)
        {
            object?[] args=[source.Handle,code,(nint)key,(nint)flags,false];
            var value=typeof(IslandWindow).GetMethod("NativeForegroundKeyMessage",Flags)!.Invoke(window,args);
            return (nint)value! == 0&&args[4] is false;
        }
        check("native key release hook belongs to the actual owning HWND",ReferenceEquals(Field<HwndSource>("nativeForegroundKeySource"),source)&&Active(),null);
        Down(Key.Return);Keyboard.ClearFocus();check("held Return keeps live WPF while focus is cleared",keys.Contains(Key.Return)&&!Active(),null);
        check("native key release remains unhandled for normal WPF processing",Message(0x101,13),null);await Task.Delay(80);
        check("native release clears Return without routed key-up and restores compositor",keys.Count==0&&Active(),null);
        Down(Key.LeftCtrl);Down(Key.RightCtrl);Message(0x101,0x11);await Task.Delay(50);
        check("left Ctrl release preserves right Ctrl held state",keys.SetEquals([Key.RightCtrl])&&!Active(),null);
        Message(0x101,0x11,1L<<24);await Task.Delay(80);check("extended Ctrl release clears right side and restores",keys.Count==0&&Active(),null);
        Down(Key.LeftShift);Down(Key.RightShift);Message(0x101,0x10,0x2aL<<16);
        check("left Shift scan code leaves right Shift held",keys.SetEquals([Key.RightShift]),null);
        Message(0x101,0x10,0x36L<<16);await Task.Delay(80);check("right Shift scan code releases final held modifier",keys.Count==0&&Active(),null);
        Down(Key.LeftAlt);Down(Key.RightAlt);Message(0x105,0x12);
        check("system left Alt release preserves right Alt",keys.SetEquals([Key.RightAlt]),null);
        Message(0x105,0x12,1L<<24);await Task.Delay(80);check("extended system Alt release clears right side",keys.Count==0&&Active(),null);
        Down(Key.Return);Down(Key.Return);check("autorepeat retains one key identity",keys.Count==1,null);
        Message(0x101,13);await Task.Delay(80);var captures=Field<int>("nativeForegroundCaptures");Message(0x101,13);await Task.Delay(80);
        check("duplicate key-up cannot enqueue another capture",keys.Count==0&&Active()&&Field<int>("nativeForegroundCaptures")==captures,null);
        Down(Key.A);Message(0x100,0x41);Message(0x101,0);Message(0x101,0x10000);Message(0x101,0x42);
        check("keydown malformed and unrelated release cannot clear a held key",keys.SetEquals([Key.A])&&!Active(),null);
        Message(0x101,0x41);await Task.Delay(80);check("ordinary letter release recovers normally",keys.Count==0&&Active(),null);
        Down(Key.Return);Message(0x101,13,1L<<24);await Task.Delay(80);check("extended numpad Enter releases the same WPF Return key",keys.Count==0&&Active(),null);
        Call("DisposeNativeForeground");
        check("controller disposal releases hook owner and retained keys",!Field<bool>("nativeForegroundEnabled")&&Field<HwndSource?>("nativeForegroundKeySource") is null&&keys.Count==0,null);
    }
}
