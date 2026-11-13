using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunInputOriginLive(IslandWindow island,string output,Action<string,bool,object?> check)
    {
        const BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic;
        var type=typeof(WindowsActivity);var pointer=type.GetField("pointerAt",flags)!;var keyboard=type.GetField("keyboardAt",flags)!;
        var target=type.GetField("pointerWindow",flags)!;var packet=type.GetField("inputPacket",flags)!;
        ((DispatcherTimer)typeof(IslandWindow).GetField("fullscreenTimer",Flags)!.GetValue(island)!).Stop();island.Hide();
        var button=new Button{Content="检查输入来源",Height=65,Margin=new Thickness(20)};
        var status=new TextBlock{Text="先点击按钮，再按 F8，再点击按钮。",Margin=new Thickness(20),FontSize=16};
        var content=new StackPanel();content.Children.Add(button);content.Children.Add(status);
        var probe=new Window{Title="Work Island · 输入来源验收",Width=540,Height=240,Left=650,Top=280,ShowInTaskbar=true,ShowActivated=false,Content=content};
        var clicks=0;var keys=0;var rawMessages=0;button.Click+=(_,_)=>clicks++;probe.PreviewKeyDown+=(_,_)=>keys++;
        probe.Show();var handle=new WindowInteropHelper(probe).Handle;
        HwndSourceHook hook=(nint hwnd,int message,nint wParam,nint lParam,ref bool handled)=>{if(message==0xff)rawMessages++;return IntPtr.Zero;};
        // Observe the actual production raw-input source, not synthetic UI events.
        var source=(HwndSource)type.GetField("inputSource",flags)!.GetValue(null)!;source.AddHook(hook);
        File.WriteAllText(Path.Combine(output,"input-ready.json"),JsonSerializer.Serialize(new{processId=RuntimeCompat.ProcessId,hwnd=handle.ToInt64(),title=probe.Title,timeoutSeconds=180},AtomicStore.JsonOptions));
        var stages=new List<object>();var watch=Stopwatch.StartNew();var stage=0;
        try
        {
            while(stage<3 && watch.Elapsed.TotalSeconds<180 && !File.Exists(Path.Combine(output,"stop-review.txt")))
            {
                await Task.Delay(100);
                var p=(long?)pointer.GetValue(null);var k=(long)keyboard.GetValue(null)!;var pointerWins=p>=k;
                var pointerTarget=(nint)target.GetValue(null)!;var effective=(nint)type.GetProperty("EffectiveWindow",flags)!.GetValue(null)!;
                var cleared=Array.TrueForAll((byte[])packet.GetValue(null)!,value=>value==0);
                var accepted=stage switch{0=>clicks>=1&&pointerWins&&pointerTarget==handle,1=>keys>=1&&!pointerWins&&effective==handle,2=>clicks>=2&&pointerWins&&pointerTarget==handle,_=>false};
                if(accepted){stages.Add(new{stage,clicks,keys,rawMessages,pointerWins,targetMatchesProbe=effective==handle,packetCleared=cleared,elapsedMilliseconds=watch.ElapsedMilliseconds});stage++;}
                status.Text=stage switch{0=>"请点击按钮",1=>"请按 F8",2=>"请再次点击按钮",_=>"输入来源切换完成"};
                File.WriteAllText(Path.Combine(output,"input-progress.json"),JsonSerializer.Serialize(new{stage,clicks,keys,rawMessages,pointerWins,targetMatchesProbe=effective==handle,packetCleared=cleared,stages},AtomicStore.JsonOptions));
            }
            check("actual WM_INPUT selects pointer, keyboard, then pointer through production adapter",stage==3&&rawMessages>=3,new{stage,clicks,keys,rawMessages,stages});
        }
        finally{source.RemoveHook(hook);probe.Close();}
        File.WriteAllText(Path.Combine(output,"input-result.json"),JsonSerializer.Serialize(new{passed=stage==3,stages,scope="Official native input in a taskbar-visible isolated WPF fixture using the production RAWINPUT adapter. No key values or packet contents retained. Release interleavings are covered separately by synthetic packet checks; not physical keyboard, real browser, tray or game acceptance."},AtomicStore.JsonOptions));
    }
}
