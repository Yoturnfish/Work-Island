using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Media;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    // Deliberately separate from the product and the deterministic regression suite.
    // Tests the installed Windows composition backend before attempting integration.
    private static int RunNativeScanProbe(string folder,bool layered=false)
    {
        folder=Path.GetFullPath(folder);
        if(Directory.Exists(folder))throw new InvalidOperationException("Use a fresh evidence directory.");
        Directory.CreateDirectory(folder);
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var exit=0;
        app.Dispatcher.InvokeAsync(async()=>
        {
            Window? host=null;NativeScanScene? scene=null;
            using var own=Process.GetCurrentProcess();var samples=new List<object>();
            try
            {
                host=new Window
                {
                    Title="Work Island native progress experiment",ShowActivated=false,
                    Left=320,Top=260,Width=400,Height=150,ResizeMode=ResizeMode.NoResize,
                    Background=layered?Brushes.Transparent:new SolidColorBrush(System.Windows.Media.Color.FromRgb(6,15,10)),
                    AllowsTransparency=layered,WindowStyle=layered?WindowStyle.None:WindowStyle.SingleBorderWindow
                };
                var reference=new PixelProgress{Width=200.4,Height=24,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(24,32,0,0),Visibility=Visibility.Collapsed};
                host.Content=new Border{Background=new SolidColorBrush(System.Windows.Media.Color.FromArgb(layered?(byte)210:(byte)255,6,15,10)),CornerRadius=new CornerRadius(20),Child=reference};host.Show();var hwnd=new WindowInteropHelper(host).Handle;
                var dpi=VisualTreeHelper.GetDpi(host);scene=new NativeScanScene(hwnd,(float)dpi.DpiScaleX,(float)dpi.DpiScaleY,!layered);
                var accent=new SolidColorBrush(System.Windows.Media.Color.FromRgb(64,255,97));
                File.WriteAllText(Path.Combine(folder,"ready.json"),JsonSerializer.Serialize(new{processId=own.Id,hwnd=hwnd.ToInt64(),layered,widthDip=200.4,heightDip=24,dpiScaleX=dpi.DpiScaleX,dpiScaleY=dpi.DpiScaleY,columns=30,rows=3,scanSeconds=2.8},AtomicStore.JsonOptions));
                await Task.Delay(1500);
                var uiId=AuditGetCurrentThreadId();
                double UiCpu()
                {
                    own.Refresh();foreach(ProcessThread thread in own.Threads)using(thread)if(thread.Id==uiId)return thread.TotalProcessorTime.TotalSeconds;
                    throw new InvalidOperationException("UI thread missing.");
                }
                for(var round=0;round<(layered?0:3);round++)foreach(var phase in new[]{"native-static","native-scan","wpf-static","wpf-scan"})
                {
                    var native=phase.StartsWith("native-");var scanning=phase.EndsWith("-scan");
                    scene.SetScanning(native&&scanning);scene.SetVisible(native);
                    reference.Visibility=native?Visibility.Collapsed:Visibility.Visible;reference.Update(.5,"tracking","matrix",accent,!scanning,30);
                    await Task.Delay(700);
                    own.Refresh();var cpu=own.TotalProcessorTime.TotalSeconds;var ui=UiCpu();var watch=Stopwatch.StartNew();
                    await Task.Delay(4000);own.Refresh();watch.Stop();var consumed=own.TotalProcessorTime.TotalSeconds-cpu;
                    samples.Add(new{round,phase,seconds=watch.Elapsed.TotalSeconds,cpuSeconds=consumed,oneCorePercent=consumed/watch.Elapsed.TotalSeconds*100,uiCpuSeconds=UiCpu()-ui});
                    File.WriteAllText(Path.Combine(folder,"samples-in-progress.json"),JsonSerializer.Serialize(samples,AtomicStore.JsonOptions));
                }
                reference.Visibility=Visibility.Collapsed;scene.SetVisible(true);scene.SetScanning(true);
                File.WriteAllText(Path.Combine(folder,"visual-ready.json"),JsonSerializer.Serialize(new{hwnd=hwnd.ToInt64(),phase="scan",inspectionSeconds=layered?30:90,method="Native 90 rounded-cell scene; local absolute gradients, one immutable animation template, no frame callbacks or business timer. Glow and full product lifecycle are not implemented in this experiment."},AtomicStore.JsonOptions));
                if(layered)await Task.Delay(30000);
                else
                {
                    await Task.Delay(20000);
                    File.WriteAllText(Path.Combine(folder,"ui-blocked.txt"),DateTimeOffset.UtcNow.ToString("O"));
                    // External screenshots verify that native scan continues while
                    // this fixture's UI thread is blocked. Never block the product.
                    Thread.Sleep(30000);
                    File.WriteAllText(Path.Combine(folder,"ui-unblocked.txt"),DateTimeOffset.UtcNow.ToString("O"));
                    await Task.Delay(40000);
                }
                File.WriteAllText(Path.Combine(folder,"native-scan-results.json"),JsonSerializer.Serialize(new
                {
                    completed=true,layered,uiThread=uiId,processorCount=Environment.ProcessorCount,samples,
                    productDllSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),
                    limitations="Isolated composition experiment only: native scan omits filled-cell glow; no product integration, wave, state transitions, dynamic DPI changes, popup/game avoidance or real input acceptance. Both paths use the same host, 200.4x24 DIP, 30x3 geometry, color and 2.8s period. CPU is own-process only, excludes DWM/GPU/system-wide cost. Four-second samples are not long-run acceptance."
                },AtomicStore.JsonOptions));
            }
            catch(Exception error){exit=1;File.WriteAllText(Path.Combine(folder,"error.txt"),error.ToString());Console.Error.WriteLine(error);}
            finally{scene?.Dispose();host?.Close();app.Shutdown();}
        });
        app.Run();return exit;
    }
}

// All ABI signatures/slots checked against the installed Windows.UI.winmd;
// evidence: test-results/native-scan-abi-20261108.txt. No external window control.
internal sealed class NativeScanScene:IDisposable
{
    private readonly List<nint> references=[];
    private readonly List<nint> animatedBrushes=[];
    private nint queue,target,animation,rootVisual;
    private bool initialized,scanning;
    private const float Width=200.4f,Height=24,Band=Width*.18f;
    internal NativeScanScene(nint hwnd,float dpiX,float dpiY,bool drawBackground=true)
    {
        try
        {
            Hr(RoInitialize(0));initialized=true;
            Hr(CreateDispatcherQueueController(new QueueOptions{Size=12,Thread=2,Apartment=2},out queue));
            using var name=new HString("Windows.UI.Composition.Compositor");var factoryId=new Guid("00000035-0000-0000-c000-000000000046");
            Hr(RoGetActivationFactory(name.Value,ref factoryId,out var factory));Keep(factory);
            var compositor=Create(factory,6);var c=Query(compositor,"b403ca50-7f8c-4e83-985f-cc45060036d8");
            var c4=Query(compositor,"ae47e78a-7910-4425-a482-a05b758adce9");var c5=Query(compositor,"48ea31ad-7fcd-4076-a79c-90cc4b852c9b");
            var root=Create(c,9);rootVisual=Query(root,Visual);Hr(Fn<PutVector>(rootVisual,36)(rootVisual,new(400,150)));
            Hr(Fn<PutVector3>(rootVisual,34)(rootVisual,new(dpiX,dpiY,1)));
            var children=Create(Query(root,"02f6bc74-ed20-4773-afe6-d49b4a93db32"),6);
            nint Color(byte alpha,byte r=64,byte g=255,byte b=97)
            {
                Hr(Fn<CreateColor>(c,8)(c,new(alpha,r,g,b),out var brush));Keep(brush);return Query(brush,Brush);
            }
            if(drawBackground)
            {
                var background=Create(c,22);var sprite=Query(background,"08e05581-1ad1-4f97-9757-402d76e4233b");Hr(Fn<PutObject>(sprite,7)(sprite,Color(255,6,15,10)));
                var bgVisual=Query(background,Visual);Hr(Fn<PutVector>(bgVisual,36)(bgVisual,new(400,150)));Hr(Fn<PutObject>(children,9)(children,bgVisual));
            }
            var shapeVisual=Create(c5,21);var shapeBase=Query(shapeVisual,Visual);Hr(Fn<PutVector>(shapeBase,36)(shapeBase,new(400,150)));
            var shapes=VectorOf(Create(Query(shapeVisual,"f2bd13c3-ba7e-4b0f-9126-ffb7536b8176"),6),"CompositionShape","b47ce2f7-9a88-42c4-9e87-2e500ca8688c");
            var filled=Color((byte)(.72*255));var unfilled=Color((byte)(.10*255));
            var scalar=Create(c,20);var scalarInterface=Query(scalar,"ae288fa9-252c-4b95-a725-bf85e38000a1");
            var easing=Query(Create(c,17),"5145e356-bf79-4ea8-8cc2-6b5b472e6c9a");
            Hr(Fn<InsertKeyFrame>(scalarInterface,7)(scalarInterface,0,0,easing));Hr(Fn<InsertKeyFrame>(scalarInterface,7)(scalarInterface,1,Width+Band,easing));
            var key=Query(scalar,"126e7f22-3ae9-4540-9a8a-deae8a4a4a84");Hr(Fn<PutLong>(key,9)(key,TimeSpan.FromSeconds(2.8).Ticks));Hr(Fn<PutInt>(key,11)(key,1));
            animation=Query(scalar,"464c4c2c-1caa-4061-9b40-e13fde1503ca");
            var cellWidth=(Width-29*2)/30;var cellHeight=(Height-4)/3;
            void AddCell(nint geometry,nint brush,float x,float y)
            {
                Hr(Fn<CreateObject>(c5,23)(c5,geometry,out var sprite));Keep(sprite);
                var spriteInterface=Query(sprite,"401b61bb-0007-4363-b1f3-6bcc003fb83e");Hr(Fn<PutObject>(spriteInterface,7)(spriteInterface,brush));
                var shape=Query(sprite,"b47ce2f7-9a88-42c4-9e87-2e500ca8688c");Hr(Fn<PutVector>(shape,9)(shape,new(x,y)));Hr(Fn<PutObject>(shapes,13)(shapes,shape));
            }
            for(var column=0;column<30;column++)for(var row=0;row<3;row++)
            {
                var x=column*(cellWidth+2);var y=row*(cellHeight+2);var isFilled=column*3+row<45;
                var rounded=Create(c5,20);var roundedInterface=Query(rounded,"8770c822-1d50-4b8b-b013-7c9a0e46935f");
                Hr(Fn<PutVector>(roundedInterface,7)(roundedInterface,new(1.5f,1.5f)));Hr(Fn<PutVector>(roundedInterface,11)(roundedInterface,new(cellWidth,cellHeight)));
                var geometry=Query(rounded,"e985217c-6a17-4207-abd8-5fd3dd612a9d");AddCell(geometry,isFilled?filled:unfilled,24+x,32+y);
                var gradient=Create(c4,8);var linear=Query(gradient,"983bc519-a9db-413c-a2d8-2a9056fc525e");
                Hr(Fn<PutInt>(Query(gradient,"899dd5a1-b4c7-4b33-a1b6-264addc26d10"),7)(Query(gradient,"899dd5a1-b4c7-4b33-a1b6-264addc26d10"),0)); // Absolute coordinates.
                Hr(Fn<PutVector>(linear,9)(linear,new(-Band-x,0)));Hr(Fn<PutVector>(linear,7)(linear,new(-x,0)));
                var stops=VectorOf(Create(Query(gradient,"1d9709e0-ffc6-4c0e-a9ab-34144d4c9098"),10),"CompositionColorGradientStop","6f00ca92-c801-4e41-9a8f-a53e20f57778");
                double[] strengths=isFilled?[0,.36,.72,1,.72,.36,0]:[0,.11,.22,.33,.22,.11,0];float[] positions=[0,.17f,.33f,.5f,.67f,.83f,1];
                for(var i=0;i<7;i++){Hr(Fn<CreateStop>(c4,7)(c4,positions[i],new((byte)(strengths[i]*255),64,255,97),out var stop));Keep(stop);Hr(Fn<PutObject>(stops,13)(stops,stop));}
                AddCell(geometry,Query(gradient,Brush),24+x,32+y);animatedBrushes.Add(Query(gradient,Object));
            }
            Hr(Fn<PutObject>(children,9)(children,shapeBase));
            var interop=Query(compositor,"29e691fa-4567-4dca-b319-d0f207eb6807");Hr(Fn<CreateTarget>(interop,3)(interop,hwnd,1,out var desktop));Keep(desktop);
            target=Query(desktop,"a1bea8ba-d726-4663-8129-6b5e7927ffa6");Hr(Fn<PutObject>(target,7)(target,rootVisual));
        }
        catch{Dispose();throw;}
    }
    internal void SetScanning(bool value)
    {
        if(scanning==value)return;using var property=new HString("Offset.X");
        foreach(var brush in animatedBrushes)
            if(value)Hr(Fn<Start>(brush,9)(brush,property.Value,animation));else Hr(Fn<Stop>(brush,10)(brush,property.Value));
        scanning=value;
    }
    internal void SetVisible(bool value)=>Hr(Fn<PutFloat>(rootVisual,23)(rootVisual,value?1:0));
    private const string Visual="117e202d-a859-4c89-873b-c2aa566788e3",Brush="ab0d7608-30c0-40e9-b568-b60a6bd1fb46",Object="bcb4ad45-7609-4550-934f-16002a68fded";
    private void Keep(nint value)=>references.Add(value);
    private nint VectorOf(nint collection,string type,string defaultInterface)
    {
        // WinRT type-system UUID-v5 derivation; QI must succeed before any call.
        var signature=$"pinterface({{913337e9-11a1-4345-a3a2-4e7f956e222d}};rc(Windows.UI.Composition.{type};{{{defaultInterface}}}))";
        var bytes=new List<byte>(new Guid("11f47ad5-7b73-42c0-abae-878b1e16adee").ToByteArray(bigEndian:true));
        bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(signature));var hash=SHA1.HashData(bytes.ToArray());hash[6]=(byte)((hash[6]&15)|0x50);hash[8]=(byte)((hash[8]&63)|0x80);
        return Query(collection,new Guid(hash.AsSpan(0,16),bigEndian:true).ToString());
    }
    private nint Query(nint value,string guid){var id=new Guid(guid);Hr(Marshal.QueryInterface(value,in id,out var result));Keep(result);return result;}
    private nint Create(nint value,int slot){Hr(Fn<CreateValue>(value,slot)(value,out var result));Keep(result);return result;}
    private static T Fn<T>(nint value,int slot)where T:Delegate
    {
        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value),slot*nint.Size));
    }
    private static void Hr(int result)=>Marshal.ThrowExceptionForHR(result);
    public void Dispose()
    {
        if(target!=0){Fn<PutObject>(target,7)(target,0);target=0;}
        for(var i=references.Count-1;i>=0;i--)Marshal.Release(references[i]);references.Clear();animatedBrushes.Clear();
        if(queue!=0){Marshal.Release(queue);queue=0;}if(initialized){RoUninitialize();initialized=false;}
    }
    private sealed class HString:IDisposable{public nint Value{get;}public HString(string value){Hr(WindowsCreateString(value,value.Length,out var p));Value=p;}public void Dispose()=>WindowsDeleteString(Value);}
    [StructLayout(LayoutKind.Sequential)]private struct QueueOptions{public int Size,Thread,Apartment;}
    [StructLayout(LayoutKind.Sequential)]private readonly record struct Vector(float X,float Y);
    [StructLayout(LayoutKind.Sequential)]private readonly record struct Vector3(float X,float Y,float Z);
    [StructLayout(LayoutKind.Sequential)]private readonly record struct ColorValue(byte A,byte R,byte G,byte B);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int CreateValue(nint self,out nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int CreateObject(nint self,nint value,out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int CreateColor(nint self,ColorValue value,out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int CreateStop(nint self,float offset,ColorValue value,out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int PutObject(nint self,nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int PutVector(nint self,Vector value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int PutVector3(nint self,Vector3 value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int PutFloat(nint self,float value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int PutInt(nint self,int value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int PutLong(nint self,long value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int InsertKeyFrame(nint self,float position,float value,nint easing);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int Start(nint self,nint property,nint animation);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int Stop(nint self,nint property);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int CreateTarget(nint self,nint hwnd,int topmost,out nint result);
    [DllImport("coremessaging.dll")]private static extern int CreateDispatcherQueueController(QueueOptions options,out nint controller);
    [DllImport("combase.dll")]private static extern int RoInitialize(uint type);
    [DllImport("combase.dll")]private static extern void RoUninitialize();
    [DllImport("combase.dll",CharSet=CharSet.Unicode)]private static extern int WindowsCreateString(string value,int length,out nint result);
    [DllImport("combase.dll")]private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")]private static extern int RoGetActivationFactory(nint name,ref Guid id,out nint factory);
}
