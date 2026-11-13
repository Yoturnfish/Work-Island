using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WorkIsland.App;

// Tests call the actual product surface implementation; no second renderer.
internal sealed class NativeDrawingSnapshot:IDisposable
{
    private const BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private readonly object surface;
    private readonly Type type;
    internal NativeDrawingSnapshot(object native)
    {
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var compositor=(nint)native.GetType().GetField("compositor",flags)!.GetValue(native)!;
        var root=(nint)native.GetType().GetField("root",flags)!.GetValue(native)!;
        type=typeof(IslandWindow).Assembly.GetType("WorkIsland.App.NativeForegroundSurface")!;
        surface=Activator.CreateInstance(type,Members,null,new object[]{compositor,root},null)!;
    }
    private int Count(string name)=>(int)type.GetProperty(name,Members)!.GetValue(surface)!;
    internal int Uploads=>Count(nameof(Uploads));
    internal int DeviceCreates=>Count(nameof(DeviceCreates));
    internal int SurfaceCreates=>Count(nameof(SurfaceCreates));
    internal int InputBitmapCreates=>Count(nameof(InputBitmapCreates));
    internal int SurfaceResizes=>Count(nameof(SurfaceResizes));
    internal int OwnedReferences=>Count(nameof(OwnedReferences));
    internal void Upload(BitmapSource bitmap,Size size)=>type.GetMethod("Upload",Members)!.Invoke(surface,new object[]{bitmap,size});
    public Task<Size> Load(string path,Size size)
    {
        BitmapSource bitmap;
        using(var stream=File.OpenRead(path))bitmap=new FormatConvertedBitmap(BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0],PixelFormats.Pbgra32,null,0);
        Upload(bitmap,size);return Task.FromResult(new Size(bitmap.PixelWidth,bitmap.PixelHeight));
    }
    public void Commit(){ /* Independent desktop compositor submits automatically. */ }
    public void Dispose()=>((IDisposable)surface).Dispose();
}