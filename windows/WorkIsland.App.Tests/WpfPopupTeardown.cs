using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

// Test-only shutdown barrier. IsOpen=false starts native popup destruction;
// it does not establish that capture and the visual source have been released.
internal static class WpfPopupTeardown
{
    internal sealed record Result(int Menus,int Awaited,bool Closed,bool Unloaded,string? RemainingCapture)
    { public bool Passed=>Closed&&Unloaded&&RemainingCapture is null; }
    internal static async Task<Result> CloseAsync(IEnumerable<ContextMenu> menus,TimeSpan timeout)
    {
        var unique=menus.Distinct().ToArray();
        var tasks=new List<Task>();var remove=new List<Action>();var awaited=0;
        try
        {
            foreach(var menu in unique)
            {
                // A never-opened form has no native source to tear down.
                // A closing form can still be loaded and visible with IsOpen=false.
                if(menu.IsLoaded||menu.IsVisible)
                {
                    awaited++;
                    var closed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var unloaded=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    RoutedEventHandler onClosed=(_,_)=>closed.TrySetResult();
                    RoutedEventHandler onUnloaded=(_,_)=>unloaded.TrySetResult();
                    menu.Closed+=onClosed;menu.Unloaded+=onUnloaded;
                    remove.Add(()=>{menu.Closed-=onClosed;menu.Unloaded-=onUnloaded;});
                    tasks.Add(closed.Task);tasks.Add(unloaded.Task);
                }
                menu.IsOpen=false;
            }
            await Task.WhenAll(tasks).WaitAsync(timeout);
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            return new(unique.Length,awaited,unique.All(m=>!m.IsOpen&&!m.IsVisible),unique.All(m=>!m.IsLoaded),Mouse.Captured?.GetType().FullName);
        }
        finally{foreach(var detach in remove)detach();}
    }
}
