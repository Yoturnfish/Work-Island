using WorkIsland.Core;
using System;
using System.Collections.Generic;


using System.Runtime.InteropServices;

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WorkIsland.App;

// Reusable native foreground texture. Surface/device/context interfaces
// verified against Microsoft SDK headers in native-drawing-headers-20261108.
internal sealed class NativeForegroundSurface:IDisposable
{
    private readonly List<nint> references=[];
    private readonly nint compositor,children;
    private nint imageVisual,surface,drawing,inputBitmap,graphicsDevice;
    private NativeScalarPulse? scaleXPulse,scaleYPulse,opacityPulse;
    private long scalePulseEpoch=-1,opacityPulseEpoch=-1;
    private double scaleTarget,scalePeriod,opacityFrom,opacityTo,opacityPeriod;
    private int pixelWidth,pixelHeight;
    private byte[] pixels=[];
    private GCHandle pinned;
    private bool disposed;
    internal int Uploads{get;private set;}
    internal int DeviceCreates{get;private set;}
    internal int SurfaceCreates{get;private set;}
    internal int InputBitmapCreates{get;private set;}
    internal int SurfaceResizes{get;private set;}
    internal int OwnedReferences=>references.Count+(inputBitmap==0?0:1);
    internal int AnimationReferences=>(scaleXPulse?.OwnedReferences??0)+(scaleYPulse?.OwnedReferences??0)+(opacityPulse?.OwnedReferences??0);
    internal int ActivePulses=>(scaleXPulse?.Attached==true?1:0)+(scaleYPulse?.Attached==true?1:0)+(opacityPulse?.Attached==true?1:0);
    internal int ScaleStarts{get;private set;}
    internal int OpacityStarts{get;private set;}
    internal int DetachedControllers{get;private set;}
    internal nint GraphicsDevice=>graphicsDevice!=0&&!disposed?graphicsDevice:throw new InvalidOperationException("Surface device is not initialized.");
    internal NativeForegroundSurface(nint compositor,nint root):this(compositor,root,0){}
    internal NativeForegroundSurface(nint compositor,nint root,nint sharedDevice)
    {
        this.compositor=compositor;
        try
        {
            children=Create(Query(root,"02f6bc74-ed20-4773-afe6-d49b4a93db32"),6);
            if(sharedDevice!=0){Marshal.AddRef(sharedDevice);references.Add(sharedDevice);graphicsDevice=sharedDevice;}
        }
        catch{Dispose();throw;}
    }
    private void Initialize(BitmapSource bitmap,Size size)
    {
        if(graphicsDevice==0)
        {
            Hr(D3D11CreateDevice(0,1,0,0x20,0,0,7,out var d3d,out _,out var immediate));references.Add(d3d);references.Add(immediate);
            var dxgi=Query(d3d,"54ec77fa-1377-44e6-8c32-88fd5f44c84c");Hr(D2D1CreateDevice(dxgi,0,out var d2d));references.Add(d2d);
            var interop=Query(compositor,"25297d5c-3ad4-4c9c-b5cf-e36a38512330");Hr(Fn<CreateObject>(interop,5)(interop,d2d,out graphicsDevice));references.Add(graphicsDevice);DeviceCreates++;
        }
        Hr(Fn<CreateSurface>(graphicsDevice,6)(graphicsDevice,new(bitmap.PixelWidth,bitmap.PixelHeight),87,1,out surface));references.Add(surface);SurfaceCreates++;
        drawing=Query(surface,"fd04e6e3-fe0c-4c3c-ab19-a07601a576ee");pixelWidth=bitmap.PixelWidth;pixelHeight=bitmap.PixelHeight;
        Hr(Fn<CreateObject>(compositor,24)(compositor,Query(surface,"1527540d-42c7-47a6-a408-668f79a90dfb"),out var brush));references.Add(brush);Hr(Fn<PutInt>(brush,11)(brush,1));
        var sprite=Create(compositor,22);Hr(Fn<PutObject>(sprite,7)(sprite,Query(brush,"ab0d7608-30c0-40e9-b568-b60a6bd1fb46")));
        imageVisual=Query(sprite,"117e202d-a859-4c89-873b-c2aa566788e3");Hr(Fn<PutVector>(imageVisual,36)(imageVisual,new((float)size.Width,(float)size.Height)));Hr(Fn<PutObject>(children,8)(children,imageVisual));
    }
    internal void Upload(BitmapSource bitmap,Size size)
    {
        RuntimeCompat.ThrowIfDisposed(disposed,this);
        if(bitmap.Format!=PixelFormats.Pbgra32)throw new ArgumentException("Upload requires premultiplied BGRA pixels.",nameof(bitmap));
        if(surface==0)Initialize(bitmap,size);
        else if(pixelWidth!=bitmap.PixelWidth||pixelHeight!=bitmap.PixelHeight)
        {
            Hr(Fn<Resize>(drawing,5)(drawing,new((uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight)));SurfaceResizes++;
            pixelWidth=bitmap.PixelWidth;pixelHeight=bitmap.PixelHeight;
            if(inputBitmap!=0){Marshal.Release(inputBitmap);inputBitmap=0;}
        }
        Hr(Fn<PutVector>(imageVisual,36)(imageVisual,new((float)size.Width,(float)size.Height)));
        var stride=pixelWidth*4;
        if(pixels.Length!=stride*pixelHeight)
        {
            if(pinned.IsAllocated)pinned.Free();pixels=new byte[stride*pixelHeight];pinned=GCHandle.Alloc(pixels,GCHandleType.Pinned);
        }
        bitmap.CopyPixels(pixels,stride,0);
        var contextId=new Guid("e8f7fe7a-191c-466d-ad95-975678bda998");
        Hr(Fn<BeginDraw>(drawing,3)(drawing,0,in contextId,out var context,out var offset));
        try
        {
            Fn<SetDpi>(context,51)(context,96,96);var clear=new Rgba(0,0,0,0);Fn<Clear>(context,47)(context,in clear);
            if(inputBitmap==0)
            {
                var properties=new BitmapProperties{Format=87,Alpha=1,DpiX=96,DpiY=96};
                Hr(Fn<CreateBitmap>(context,4)(context,new((uint)pixelWidth,(uint)pixelHeight),pinned.AddrOfPinnedObject(),(uint)stride,in properties,out inputBitmap));InputBitmapCreates++;
            }
            else Hr(Fn<CopyMemory>(inputBitmap,10)(inputBitmap,0,pinned.AddrOfPinnedObject(),(uint)stride));
            var destination=new Rectangle(offset.X,offset.Y,offset.X+pixelWidth,offset.Y+pixelHeight);var source=new Rectangle(0,0,pixelWidth,pixelHeight);
            Fn<DrawBitmap>(context,26)(context,inputBitmap,in destination,1,1,in source);
        }
        finally{try{Hr(Fn<EndDraw>(drawing,4)(drawing));}finally{Marshal.Release(context);}}
        Uploads++;
    }
    internal void SetVisible(bool visible)
    {
        RuntimeCompat.ThrowIfDisposed(disposed,this);
        // A Composition base-property write cancels that property's explicit
        // animation. The live opacity timeline itself makes this plane visible.
        if(visible&&opacityPulse is not null)return;
        try{if(!visible)StopPulses();}
        finally{if(imageVisual!=0)Hr(Fn<PutFloat>(imageVisual,23)(imageVisual,visible?1:0));}
    }
    internal void PlaceOnTop(){RuntimeCompat.ThrowIfDisposed(disposed,this);Hr(Fn<PutObject>(children,11)(children,imageVisual));Hr(Fn<PutObject>(children,9)(children,imageVisual));}
    internal void BreatheScale(long epoch,double to,double period,System.Windows.Point center)
    {
        RuntimeCompat.ThrowIfDisposed(disposed,this);Hr(Fn<PutVector3>(imageVisual,13)(imageVisual,new((float)center.X,(float)center.Y,0)));
        if(scaleXPulse?.Attached==true&&scaleYPulse?.Attached==true&&scalePulseEpoch==epoch&&scaleTarget==to&&scalePeriod==period)return;
        try{StopPulse(ref scaleXPulse);}finally{StopPulse(ref scaleYPulse);}
        var phase=PulsePhase.At(epoch,period);
        try{scaleXPulse=new(compositor,imageVisual,"Scale.X",1,to,period,phase);scaleYPulse=new(compositor,imageVisual,"Scale.Y",1,to,period,phase);scalePulseEpoch=epoch;scaleTarget=to;scalePeriod=period;ScaleStarts++;}
        catch{try{StopPulse(ref scaleXPulse);}finally{StopPulse(ref scaleYPulse);}throw;}
    }
    internal void BreatheOpacity(long epoch,double from,double to,double period)
    {
        RuntimeCompat.ThrowIfDisposed(disposed,this);
        if(opacityPulse?.Attached==true&&opacityPulseEpoch==epoch&&opacityFrom==from&&opacityTo==to&&opacityPeriod==period)return;
        StopPulse(ref opacityPulse);opacityPulse=new(compositor,imageVisual,"Opacity",from,to,period,PulsePhase.At(epoch,period));opacityPulseEpoch=epoch;opacityFrom=from;opacityTo=to;opacityPeriod=period;OpacityStarts++;
    }
    private void StopPulse(ref NativeScalarPulse? pulse)
    {
        var old=pulse;pulse=null;
        try{old?.Dispose();}finally{if(old?.ControllerAttachedAfterStop==false)DetachedControllers++;}
    }
    private void StopPulses()
    {
        try{StopPulse(ref scaleXPulse);}
        finally{try{StopPulse(ref scaleYPulse);}finally{StopPulse(ref opacityPulse);}}
        if(imageVisual!=0)Hr(Fn<PutVector3>(imageVisual,34)(imageVisual,new(1,1,1)));
        scalePulseEpoch=opacityPulseEpoch=-1;
    }

    public void Dispose()
    {
        if(disposed)return;disposed=true;
        try{StopPulses();}
        catch(COMException){ /* Device loss must not prevent releasing owned resources. */ }
        finally
        {
            if(imageVisual!=0){Fn<PutObject>(children,11)(children,imageVisual);imageVisual=0;}
            if(inputBitmap!=0){Marshal.Release(inputBitmap);inputBitmap=0;}
            for(var i=references.Count-1;i>=0;i--)Marshal.Release(references[i]);references.Clear();
            if(pinned.IsAllocated)pinned.Free();pixels=[];surface=drawing=graphicsDevice=0;
        }
    }
    private nint Query(nint pointer,string guid){var iid=new Guid(guid);Hr(RuntimeCompat.QueryInterface(pointer,iid,out var result));references.Add(result);return result;}
    private nint Create(nint pointer,int slot){Hr(Fn<CreateValue>(pointer,slot)(pointer,out var result));references.Add(result);return result;}
    private static T Fn<T>(nint pointer,int slot)where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(pointer),slot*IntPtr.Size));
    private static void Hr(int value)=>Marshal.ThrowExceptionForHR(value);
    [StructLayout(LayoutKind.Sequential)]private readonly record struct Vector(float X,float Y);
    [StructLayout(LayoutKind.Sequential)]private readonly record struct Vector3(float X,float Y,float Z);
    [StructLayout(LayoutKind.Sequential)]private readonly record struct PixelSize(uint Width,uint Height);
    [StructLayout(LayoutKind.Sequential)]private readonly record struct Point(int X,int Y);
    [StructLayout(LayoutKind.Sequential)]private readonly record struct Rgba(float R,float G,float B,float A);
    [StructLayout(LayoutKind.Sequential)]private readonly record struct Rectangle(float Left,float Top,float Right,float Bottom);
    [StructLayout(LayoutKind.Sequential)]private struct BitmapProperties{public int Format,Alpha;public float DpiX,DpiY;}
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int CreateValue(nint self,out nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int CreateObject(nint self,nint value,out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int CreateSurface(nint self,Vector size,int format,int alpha,out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int BeginDraw(nint self,nint rect,in Guid iid,out nint result,out Point offset);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int EndDraw(nint self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int Resize(nint self,PixelSize size);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int CopyMemory(nint self,nint destination,nint source,uint stride);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int CreateBitmap(nint self,PixelSize size,nint data,uint stride,in BitmapProperties properties,out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate void DrawBitmap(nint self,nint bitmap,in Rectangle destination,float opacity,int interpolation,in Rectangle source);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate void Clear(nint self,in Rgba color);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate void SetDpi(nint self,float x,float y);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int PutObject(nint self,nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int PutVector(nint self,Vector value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int PutVector3(nint self,Vector3 value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int PutInt(nint self,int value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int PutFloat(nint self,float value);
    [DllImport("d3d11.dll")]private static extern int D3D11CreateDevice(nint adapter,int type,nint software,uint flags,nint levels,uint levelCount,uint version,out nint device,out int featureLevel,out nint immediate);
    [DllImport("d2d1.dll")]private static extern int D2D1CreateDevice(nint dxgi,nint properties,out nint device);
}
