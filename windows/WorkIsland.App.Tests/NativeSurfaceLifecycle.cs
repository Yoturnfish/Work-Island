using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunNativeSurfaceLifecycle(IslandWindow window,string output,Action<string,bool,object?> check)
    {
        T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        var engine=Field<TrackingEngine>("engine");engine.Data.Settings.ReduceMotion=false;engine.Data.Settings.Rules=[new("surface-lifecycle.exe","Surface lifecycle")];
        engine.ResetObservation();engine.Observe(DateTimeOffset.Now,new("surface-lifecycle.exe","Surface lifecycle",0));typeof(IslandWindow).GetMethod("Render",Flags|BindingFlags.DeclaredOnly)!.Invoke(window,null);await Task.Delay(450);
        var progress=Field<PixelProgress>("GoalProgress");typeof(PixelProgress).GetMethod("EnableNativeScan",Flags)!.Invoke(progress,[window,Field<FrameworkElement>("PageHost")]);progress.Update(.5,"tracking","matrix",Field<SolidColorBrush>("statusBrush"),false,30);
        var native=typeof(PixelProgress).GetField("nativeScan",Flags)!.GetValue(progress)??throw new InvalidOperationException("Native scan unavailable");native.GetType().GetMethod("SetScanning",Flags)!.Invoke(native,[false]);native.GetType().GetMethod("SetOpacity",Flags)!.Invoke(native,[0d]);
        using var surface=new NativeDrawingSnapshot(native);
        BitmapSource Bitmap(int width,int height,byte green)
        {
            var bitmap=new WriteableBitmap(width,height,96,96,PixelFormats.Pbgra32,null);var pixels=new byte[width*height*4];
            for(var i=0;i<pixels.Length;i+=4){pixels[i]=32;pixels[i+1]=green;pixels[i+2]=64;pixels[i+3]=255;}
            bitmap.WritePixels(new Int32Rect(0,0,width,height),pixels,width*4,0);return bitmap;
        }
        var first=Bitmap(100,30,120);surface.Upload(first,new Size(100,30));var owned=surface.OwnedReferences;
        for(var i=0;i<80;i++)surface.Upload(Bitmap(100,30,(byte)(120+i)),new Size(100,30));
        check("repeated foreground updates reuse native device, surface, bitmap and ownership",surface.DeviceCreates==1&&surface.SurfaceCreates==1&&surface.InputBitmapCreates==1&&surface.OwnedReferences==owned,new{surface.Uploads,surface.OwnedReferences,owned});
        var actual=typeof(NativeDrawingSnapshot).GetField("surface",Flags)!.GetValue(surface)!;
        var field=actual.GetType().GetField("surface",Flags)!;
        var allSizes=true;
        foreach(var size in new[]{new Size(120,40),new Size(80,22),new Size(140,50),new Size(100,30)})
        {
            surface.Upload(Bitmap((int)size.Width,(int)size.Height,170),size);
            var pointer=(nint)field.GetValue(actual)!;var get=Marshal.GetDelegateForFunctionPointer<ActualSurfaceSize>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(pointer),8*IntPtr.Size));
            Marshal.ThrowExceptionForHR(get(pointer,out var nativeSize));allSizes&=nativeSize.Width==size.Width&&nativeSize.Height==size.Height;
        }
        check("native drawing surface reports every requested resized pixel size",allSizes&&surface.SurfaceResizes==4&&surface.DeviceCreates==1&&surface.SurfaceCreates==1&&surface.OwnedReferences==owned,new{allSizes,surface.SurfaceResizes,surface.InputBitmapCreates,surface.OwnedReferences});
        surface.Dispose();surface.Dispose();
        var pinned=(GCHandle)actual.GetType().GetField("pinned",Flags)!.GetValue(actual)!;
        check("surface disposal releases native ownership and pinned upload buffer",surface.OwnedReferences==0&&!pinned.IsAllocated,new{surface.OwnedReferences,pinned.IsAllocated});
        var rejected=false;try{surface.Upload(first,new Size(100,30));}catch(TargetInvocationException error)when(error.InnerException is ObjectDisposedException){rejected=true;}
        check("disposed surface rejects upload before native access",rejected,null);
        File.WriteAllText(Path.Combine(output,"surface-lifecycle.json"),JsonSerializer.Serialize(new{uploads=surface.Uploads,surface.DeviceCreates,surface.SurfaceCreates,surface.InputBitmapCreates,surface.SurfaceResizes,allSizes,ownedAfterDispose=surface.OwnedReferences,pinnedAfterDispose=pinned.IsAllocated,scope="Actual product NativeForegroundSurface implementation and native drawing-surface Size getter. Repeated same-size copies, four real surface resizes and idempotent release; no physical monitor/DPI acceptance."},AtomicStore.JsonOptions));
    }
    [StructLayout(LayoutKind.Sequential)]private readonly record struct ActualSurfaceVector(float Width,float Height);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int ActualSurfaceSize(nint self,out ActualSurfaceVector size);
}
