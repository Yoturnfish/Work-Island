using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunNativeDisposalChecks(IslandWindow window,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        var engine=Field<TrackingEngine>("engine");engine.Data.Settings.Rules=[new("disposal.exe","Disposal")];engine.Data.Settings.ReduceMotion=false;
        engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new("disposal.exe","Disposal",0));typeof(IslandWindow).GetMethod("Render",Flags|BindingFlags.DeclaredOnly)!.Invoke(window,null);await Task.Delay(450);
        var progress=Field<PixelProgress>("GoalProgress");typeof(PixelProgress).GetMethod("EnableNativeScan",Flags)!.Invoke(progress,[window,Field<FrameworkElement>("PageHost")]);
        progress.Update(.5,"tracking","matrix",Field<SolidColorBrush>("statusBrush"),false,30);
        var nativeField=typeof(PixelProgress).GetField("nativeScan",Flags)!;var native=nativeField.GetValue(progress)??throw new InvalidOperationException("Native scan unavailable");
        var brushes=(List<nint>)native.GetType().GetField("brushes",Flags)!.GetValue(native)!;var iid=new Guid("0bb3784c-346b-4a7c-966b-7310966553d5");
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(brushes[0],in iid,out var observer));
        const string property="Offset.X";Marshal.ThrowExceptionForHR(DisposalCreateString(property,property.Length,out var name));
        try
        {
            var get=Marshal.GetDelegateForFunctionPointer<DisposalController>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(observer),6*nint.Size));
            Marshal.ThrowExceptionForHR(get(observer,name,out var before));check("native disposal fixture has a real running animation controller",before!=0,null);if(before!=0)Marshal.Release(before);
            // Retain only a read-only brush observer outside the component. Its
            // animation must be explicitly stopped when the component disposes.
            nativeField.SetValue(progress,null);((IDisposable)native).Dispose();
            Marshal.ThrowExceptionForHR(get(observer,name,out var after));
            check("disposing native progress stops the retained brush animation",after==0,new{animationControllerPresentAfterDispose=after!=0});if(after!=0)Marshal.Release(after);
            ((IDisposable)native).Dispose();
        }
        finally{DisposalDeleteString(name);Marshal.Release(observer);}
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int DisposalController(nint self,nint property,out nint controller);
    [DllImport("combase.dll",EntryPoint="WindowsCreateString",CharSet=CharSet.Unicode)]private static extern int DisposalCreateString(string text,int length,out nint value);
    [DllImport("combase.dll",EntryPoint="WindowsDeleteString")]private static extern int DisposalDeleteString(nint value);
}
