using WorkIsland.Core;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace WorkIsland.App;

// A scan-only visual above the existing WPF geometry/glow, in the same HWND.
// ABI contracts: installed Windows.UI.winmd, native-scan-*-20261108.txt.
// No UI frame timer. Geometry/progress/color/presentation updates are explicit.
internal sealed class NativeProgressScan : IDisposable
{
    internal readonly record struct Cell(Rect Bounds, double RadiusX, double RadiusY, bool Filled);
    private sealed record PaintedCell(Cell Geometry,nint[] Stops)
    {
        public bool Filled=Geometry.Filled;
        public Color? Color;
    }
    private readonly List<nint> references=[],cellReferences=[],brushes=[];
    private readonly List<PaintedCell> cells=[];
    private nint compositor,compositor4,compositor5,root,drawing,shapes,clipGeometry,target,animation;
    private nint hostRoot,hostChildren,parentChildren;
    private bool initialized,disposed;
    private double width,band;
    private double phaseOrigin;
    private long phaseTimestamp;
    private bool phaseFrozen;
    private Color color;
    internal bool Active { get; private set; }
    internal int ScanStarts { get; private set; }
    internal int CellCount=>cells.Count;
    internal static TimeSpan ScanDuration=>TimeSpan.FromSeconds(2.8);
    internal NativeProgressScan CreateSibling()=>new(this);
    internal NativeForegroundSurface CreateForegroundSurface()=>new(compositor,root);
    internal NativeForegroundSurface CreateSharedForegroundSurface(NativeForegroundSurface source)=>new(compositor,root,source.GraphicsDevice);
    internal float PresentationOpacity { get {Hr(Fn<GetFloat>(root,22)(root,out var value));return value;} }
    internal NativeProgressScan(nint hwnd)
    {
        try
        {
            Hr(RoInitialize(0));initialized=true;HostBackdrop.EnsureDispatcherQueue();
            using var name=new HString("Windows.UI.Composition.Compositor");var factoryId=new Guid("00000035-0000-0000-c000-000000000046");
            Hr(RoGetActivationFactory(name.Value,ref factoryId,out var factory));references.Add(factory);
            compositor=Query(Create(factory,6),"b403ca50-7f8c-4e83-985f-cc45060036d8");
            compositor4=Query(compositor,"ae47e78a-7910-4425-a482-a05b758adce9");compositor5=Query(compositor,"48ea31ad-7fcd-4076-a79c-90cc4b852c9b");
            InitializeVisual();
            var host=Create(compositor,9);hostRoot=Query(host,Visual);
            hostChildren=Create(Query(host,"02f6bc74-ed20-4773-afe6-d49b4a93db32"),6);
            Hr(Fn<PutObject>(hostChildren,9)(hostChildren,root));
            var interop=Query(compositor,"29e691fa-4567-4dca-b319-d0f207eb6807");Hr(Fn<CreateTarget>(interop,3)(interop,hwnd,1,out var desktop));references.Add(desktop);
            target=Query(desktop,"a1bea8ba-d726-4663-8129-6b5e7927ffa6");Hr(Fn<PutObject>(target,7)(target,hostRoot));
        }
        catch{Dispose();throw;}
    }
    private NativeProgressScan(NativeProgressScan host)
    {
        try
        {
            RuntimeCompat.ThrowIfDisposed(host.disposed,host);
            if(host.hostChildren==0)throw new InvalidOperationException("Sibling scans require the original HWND host.");
            Hr(RoInitialize(0));initialized=true;
            compositor=Query(host.compositor,"b403ca50-7f8c-4e83-985f-cc45060036d8");
            compositor4=Query(compositor,"ae47e78a-7910-4425-a482-a05b758adce9");compositor5=Query(compositor,"48ea31ad-7fcd-4076-a79c-90cc4b852c9b");
            InitializeVisual();
            parentChildren=host.hostChildren;Marshal.AddRef(parentChildren);references.Add(parentChildren);
            Hr(Fn<PutObject>(parentChildren,9)(parentChildren,root));
        }
        catch{Dispose();throw;}
    }
    private void InitializeVisual()
    {
            var container=Create(compositor,9);root=Query(container,Visual);
            var children=Create(Query(container,"02f6bc74-ed20-4773-afe6-d49b4a93db32"),6);
            var shape=Create(compositor5,21);drawing=Query(shape,Visual);
            shapes=VectorOf(Create(Query(shape,"f2bd13c3-ba7e-4b0f-9126-ffb7536b8176"),6),"CompositionShape","b47ce2f7-9a88-42c4-9e87-2e500ca8688c",references);
            Hr(Fn<PutObject>(children,9)(children,drawing));
            var rounded=Create(compositor5,20);clipGeometry=Query(rounded,"8770c822-1d50-4b8b-b013-7c9a0e46935f");
            var clipFactory=Query(compositor,"7a38b2bd-cec8-4eeb-830f-d8d07aedebc3");
            Hr(Fn<CreateObject>(clipFactory,7)(clipFactory,Query(rounded,"e985217c-6a17-4207-abd8-5fd3dd612a9d"),out var clip));references.Add(clip);
            Hr(Fn<PutObject>(root,15)(root,Query(clip,"1ccd2a52-cfc7-4ace-9983-146bb8eb6a3c")));
            SetOpacity(0);
    }
    internal void UpdateGeometry(IReadOnlyList<Cell> geometry,double nextWidth,double nextBand,Color nextColor)
    {
        RuntimeCompat.ThrowIfDisposed(disposed,this);
        var rebuild=cells.Count!=geometry.Count||width!=nextWidth||band!=nextBand;
        if(!rebuild)for(var i=0;i<cells.Count;i++)
            if(cells[i].Geometry.Bounds!=geometry[i].Bounds||cells[i].Geometry.RadiusX!=geometry[i].RadiusX||cells[i].Geometry.RadiusY!=geometry[i].RadiusY){rebuild=true;break;}
        if(rebuild)
        {
            var restart=Active;var phase=EstimatedPhase;SetScanning(false);Hr(Fn<Clear>(shapes,15)(shapes));Release(cellReferences);cells.Clear();brushes.Clear();
            width=nextWidth;band=nextBand;
            var scalar=Create(compositor,20,cellReferences);var key=Query(scalar,"126e7f22-3ae9-4540-9a8a-deae8a4a4a84",cellReferences);
            var easing=Query(Create(compositor,17,cellReferences),"5145e356-bf79-4ea8-8cc2-6b5b472e6c9a",cellReferences);
            Hr(Fn<InsertKeyFrame>(scalar,7)(scalar,0,0,easing));Hr(Fn<InsertKeyFrame>(scalar,7)(scalar,1,(float)(width+band),easing));
            Hr(Fn<PutLong>(key,9)(key,ScanDuration.Ticks));Hr(Fn<PutInt>(key,11)(key,1));
            animation=Query(scalar,"464c4c2c-1caa-4061-9b40-e13fde1503ca",cellReferences);
            foreach(var cell in geometry)BuildCell(cell);
            UpdateColorAndFill(geometry,nextColor);
            if(restart)StartAtPhase(phase);
        }
        else UpdateColorAndFill(geometry,nextColor);
    }
    private void BuildCell(Cell cell)
    {
        var rounded=Create(compositor5,20,cellReferences);
        Hr(Fn<PutVector>(rounded,7)(rounded,new((float)cell.RadiusX,(float)cell.RadiusY)));
        Hr(Fn<PutVector>(rounded,11)(rounded,new((float)cell.Bounds.Width,(float)cell.Bounds.Height)));
        var gradient=Create(compositor4,8,cellReferences);
        var mapping=Query(gradient,"899dd5a1-b4c7-4b33-a1b6-264addc26d10",cellReferences);Hr(Fn<PutInt>(mapping,7)(mapping,0));
        Hr(Fn<PutVector>(gradient,9)(gradient,new((float)(-band-cell.Bounds.X),0)));
        Hr(Fn<PutVector>(gradient,7)(gradient,new((float)-cell.Bounds.X,0)));
        var stops=VectorOf(Create(Query(gradient,"1d9709e0-ffc6-4c0e-a9ab-34144d4c9098",cellReferences),10,cellReferences),"CompositionColorGradientStop","6f00ca92-c801-4e41-9a8f-a53e20f57778",cellReferences);
        var stopPointers=new nint[7];float[] positions=[0,.17f,.33f,.5f,.67f,.83f,1];
        for(var i=0;i<7;i++)
        {
            Hr(Fn<CreateStop>(compositor4,7)(compositor4,positions[i],new(0,0,0,0),out var stop));cellReferences.Add(stop);stopPointers[i]=stop;
            Hr(Fn<PutObject>(stops,13)(stops,stop));
        }
        Hr(Fn<CreateObject>(compositor5,23)(compositor5,Query(rounded,"e985217c-6a17-4207-abd8-5fd3dd612a9d",cellReferences),out var sprite));cellReferences.Add(sprite);
        Hr(Fn<PutObject>(sprite,7)(sprite,Query(gradient,Brush,cellReferences)));
        var shape=Query(sprite,"b47ce2f7-9a88-42c4-9e87-2e500ca8688c",cellReferences);
        Hr(Fn<PutVector>(shape,9)(shape,new((float)cell.Bounds.X,(float)cell.Bounds.Y)));Hr(Fn<PutObject>(shapes,13)(shapes,shape));
        brushes.Add(Query(gradient,CompositionObject,cellReferences));cells.Add(new(cell,stopPointers));
    }
    private void UpdateColorAndFill(IReadOnlyList<Cell> geometry,Color nextColor)
    {
        for(var i=0;i<cells.Count;i++)
        {
            var cell=cells[i];var filled=geometry[i].Filled;if(cell.Color==nextColor&&cell.Filled==filled)continue;
            double[] strengths=filled?[0,.36,.72,1,.72,.36,0]:[0,.11,.22,.33,.22,.11,0];
            for(var stop=0;stop<7;stop++)Hr(Fn<PutColor>(cell.Stops[stop],7)(cell.Stops[stop],new((byte)(strengths[stop]*255),nextColor.R,nextColor.G,nextColor.B)));
            cell.Filled=filled;cell.Color=nextColor;
        }
        color=nextColor;
    }
    internal void UpdateColor(Color nextColor)
    {
        if(color==nextColor)return;
        UpdateColorAndFill(cells.Select(cell=>cell.Geometry with{Filled=cell.Filled}).ToArray(),nextColor);
    }
    internal void SetPresentation(Matrix transform,double opacity,RectangleGeometry clip,Size windowSize,DpiScale dpi)
    {
        // The product uses only axis-aligned scale/translation. Explicitly reject
        // unsupported transforms rather than drawing a mismatched native layer.
        if(Math.Abs(transform.M12)>.00001||Math.Abs(transform.M21)>.00001||transform.M11<=0||transform.M22<=0)throw new NotSupportedException("Native scan requires positive axis-aligned presentation.");
        if(hostRoot!=0)Hr(Fn<PutVector>(hostRoot,36)(hostRoot,new((float)(windowSize.Width*dpi.DpiScaleX),(float)(windowSize.Height*dpi.DpiScaleY))));
        Hr(Fn<PutVector3>(root,34)(root,new((float)dpi.DpiScaleX,(float)dpi.DpiScaleY,1)));
        Hr(Fn<PutVector>(root,36)(root,new((float)windowSize.Width,(float)windowSize.Height)));
        Hr(Fn<PutVector>(drawing,36)(drawing,new((float)Math.Max(windowSize.Width,width),(float)windowSize.Height)));
        Hr(Fn<PutVector3>(drawing,34)(drawing,new((float)transform.M11,(float)transform.M22,1)));
        Hr(Fn<PutVector3>(drawing,21)(drawing,new((float)transform.OffsetX,(float)transform.OffsetY,0)));
        Hr(Fn<PutVector>(clipGeometry,9)(clipGeometry,new((float)clip.Rect.X,(float)clip.Rect.Y)));
        Hr(Fn<PutVector>(clipGeometry,11)(clipGeometry,new((float)clip.Rect.Width,(float)clip.Rect.Height)));
        Hr(Fn<PutVector>(clipGeometry,7)(clipGeometry,new((float)clip.RadiusX,(float)clip.RadiusY)));
        SetOpacity(opacity);
    }
    internal void SetOpacity(double opacity)=>Hr(Fn<PutFloat>(root,23)(root,(float)RuntimeCompat.Clamp(opacity,0,1)));
    internal void SetScanning(bool value)
    {
        if(drawing!=0)Hr(Fn<PutFloat>(drawing,23)(drawing,value?1:0));
        if(Active==value)return;using var property=new HString("Offset.X");
        foreach(var brush in brushes)
            if(value)Hr(Fn<Start>(brush,9)(brush,property.Value,animation));else Hr(Fn<Stop>(brush,10)(brush,property.Value));
        Active=value;if(value){ScanStarts++;phaseOrigin=0;phaseTimestamp=Stopwatch.GetTimestamp();phaseFrozen=false;}
    }
    internal void FreezePhaseForReview(float phase)
    {
        if(!Active)throw new InvalidOperationException("Scan must be active before freezing a review frame.");
        SeekPhase(phase,false);
        phaseOrigin=RuntimeCompat.Clamp(phase,0,1);phaseTimestamp=Stopwatch.GetTimestamp();phaseFrozen=true;
    }
    // Composition getters expose the submitted value on this Windows runtime,
    // not the advancing compositor-thread position. Use a monotonic estimate
    // only for renderer handoff; visual continuity is separately screen-tested.
    internal double EstimatedPhase=>phaseFrozen||!Active?phaseOrigin:(phaseOrigin+RuntimeCompat.GetElapsedTime(phaseTimestamp).TotalSeconds/ScanDuration.TotalSeconds)%1;
    internal double ReadControllerProgressForReview()
    {
        RuntimeCompat.ThrowIfDisposed(disposed,this);
        if(!Active||brushes.Count==0)return 0;
        var controller=Controller(brushes[0]);
        try{Hr(Fn<GetFloat>(controller,8)(controller,out var value));return RuntimeCompat.Clamp(value,0,1);}
        finally{Marshal.Release(controller);}
    }
    internal void StartAtPhase(double phase)
    {
        RuntimeCompat.ThrowIfDisposed(disposed,this);
        if(!RuntimeCompat.IsFinite(phase))throw new ArgumentOutOfRangeException(nameof(phase));
        SetScanning(true);SeekPhase(phase,true);
        phaseOrigin=RuntimeCompat.Clamp(phase,0,1)%1;phaseTimestamp=Stopwatch.GetTimestamp();phaseFrozen=false;
    }
    private nint Controller(nint brush)
    {
        var iid=new Guid("0bb3784c-346b-4a7c-966b-7310966553d5");
        Hr(RuntimeCompat.QueryInterface(brush,iid,out var source));
        using var property=new HString("Offset.X");
        try
        {
            Hr(Fn<GetController>(source,6)(source,property.Value,out var controller));
            return controller!=0?controller:throw new InvalidOperationException("Native scan has no animation controller.");
        }
        finally{Marshal.Release(source);}
    }
    private void SeekPhase(double phase,bool resume)
    {
        if(!RuntimeCompat.IsFinite(phase))throw new ArgumentOutOfRangeException(nameof(phase));
        foreach(var brush in brushes)
        {
            var controller=Controller(brush);
            try
            {
                Hr(Fn<Clear>(controller,12)(controller));Hr(Fn<PutFloat>(controller,9)(controller,(float)RuntimeCompat.Clamp(phase,0,1)));
                if(resume)Hr(Fn<Clear>(controller,13)(controller));
            }
            finally{Marshal.Release(controller);}
        }
    }
    private const string Visual="117e202d-a859-4c89-873b-c2aa566788e3",Brush="ab0d7608-30c0-40e9-b568-b60a6bd1fb46",CompositionObject="bcb4ad45-7609-4550-934f-16002a68fded";
    private nint VectorOf(nint collection,string type,string defaultInterface,List<nint> owner)
    {
        var signature=$"pinterface({{913337e9-11a1-4345-a3a2-4e7f956e222d}};rc(Windows.UI.Composition.{type};{{{defaultInterface}}}))";
        var input=new List<byte>(RuntimeCompat.GuidBigEndianBytes(new Guid("11f47ad5-7b73-42c0-abae-878b1e16adee")));input.AddRange(Encoding.UTF8.GetBytes(signature));
        var hash=RuntimeCompat.Sha1(input.ToArray());hash[6]=(byte)((hash[6]&15)|0x50);hash[8]=(byte)((hash[8]&63)|0x80);
        return Query(collection,RuntimeCompat.GuidFromBigEndianBytes(hash.Take(16).ToArray()).ToString(),owner);
    }
    private nint Query(nint pointer,string guid,List<nint>? owner=null){var iid=new Guid(guid);Hr(RuntimeCompat.QueryInterface(pointer,iid,out var result));(owner??references).Add(result);return result;}
    private nint Create(nint pointer,int slot,List<nint>? owner=null){Hr(Fn<CreateValue>(pointer,slot)(pointer,out var result));(owner??references).Add(result);return result;}
    private static T Fn<T>(nint pointer,int slot)where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(pointer),slot*IntPtr.Size));
    private static void Hr(int result)=>Marshal.ThrowExceptionForHR(result);
    private static void Release(List<nint> owner){for(var i=owner.Count-1;i>=0;i--)Marshal.Release(owner[i]);owner.Clear();}
    public void Dispose()
    {
        if(disposed)return;
        // Detaching the visual is insufficient when another COM observer still
        // retains a brush. Explicitly remove its forever animation first.
        try{SetScanning(false);}catch(COMException){ /* Device loss must not prevent releasing owned references. */ }
        disposed=true;
        if(parentChildren!=0&&root!=0){Fn<PutObject>(parentChildren,11)(parentChildren,root);parentChildren=0;}
        if(target!=0){Fn<PutObject>(target,7)(target,0);target=0;}
        Release(cellReferences);Release(references);cells.Clear();brushes.Clear();Active=false;
        if(initialized){RoUninitialize();initialized=false;}
    }
    private sealed class HString:IDisposable{public nint Value{get;}public HString(string value){Hr(WindowsCreateString(value,value.Length,out var p));Value=p;}public void Dispose()=>WindowsDeleteString(Value);}
    [StructLayout(LayoutKind.Sequential)]private readonly record struct Vector(float X,float Y);
    [StructLayout(LayoutKind.Sequential)]private readonly record struct Vector3(float X,float Y,float Z);
    [StructLayout(LayoutKind.Sequential)]private readonly record struct ColorValue(byte A,byte R,byte G,byte B);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int CreateValue(nint self,out nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int CreateObject(nint self,nint value,out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int CreateStop(nint self,float offset,ColorValue value,out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int PutObject(nint self,nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int PutVector(nint self,Vector value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int PutVector3(nint self,Vector3 value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int PutFloat(nint self,float value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int GetFloat(nint self,out float value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int PutColor(nint self,ColorValue value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int PutInt(nint self,int value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int PutLong(nint self,long value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int Clear(nint self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int InsertKeyFrame(nint self,float progress,float value,nint easing);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int Start(nint self,nint property,nint animation);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int Stop(nint self,nint property);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int GetController(nint self,nint property,out nint controller);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int CreateTarget(nint self,nint window,int topmost,out nint result);
    [DllImport("combase.dll")]private static extern int RoInitialize(uint type);
    [DllImport("combase.dll")]private static extern void RoUninitialize();
    [DllImport("combase.dll",CharSet=CharSet.Unicode)]private static extern int WindowsCreateString(string text,int length,out nint value);
    [DllImport("combase.dll")]private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")]private static extern int RoGetActivationFactory(nint name,ref Guid iid,out nint factory);
}
