using WorkIsland.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WorkIsland.App;

internal static class PulsePhase
{
    internal static double At(long epoch,double period)=>(RuntimeCompat.GetElapsedTime(epoch).TotalSeconds/period)%1;
    internal static double Value(double phase,double from,double to)=>from+(to-from)*(.5-.5*Math.Cos(phase*2*Math.PI));
}

// Scalar animation ABI shared with the existing scan backend and verified in
// native-scan-*-20261108.txt. Phase is a monotonic handoff estimate; the runtime
// getter does not expose the advancing compositor-thread value on this host.
internal sealed class NativeScalarPulse:IDisposable
{
    private readonly List<nint> references=[];
    private nint composition,property;
    private bool started,disposed;
    internal int OwnedReferences=>references.Count;
    internal bool Attached
    {
        get
        {
            if(disposed||!started)return false;
            var id=new Guid("0bb3784c-346b-4a7c-966b-7310966553d5");Hr(RuntimeCompat.QueryInterface(composition,id,out var source));
            try{Hr(Fn<GetController>(source,6)(source,property,out var current));if(current==0)return false;Marshal.Release(current);return true;}
            finally{Marshal.Release(source);}
        }
    }
    internal bool? ControllerAttachedAfterStop{get;private set;}
    internal NativeScalarPulse(nint compositor,nint visual,string name,double from,double to,double period,double phase)
    {
        try
        {
            composition=Query(visual,"bcb4ad45-7609-4550-934f-16002a68fded");
            Hr(WindowsCreateString(name,name.Length,out property));
            nint Create(int slot){Hr(Fn<CreateValue>(compositor,slot)(compositor,out var value));references.Add(value);return value;}
            var scalar=Create(20);var key=Query(scalar,"126e7f22-3ae9-4540-9a8a-deae8a4a4a84");var linear=Query(Create(17),"5145e356-bf79-4ea8-8cc2-6b5b472e6c9a");
            for(var index=0;index<=128;index++)Hr(Fn<InsertKeyFrame>(scalar,7)(scalar,index/128f,(float)PulsePhase.Value(index/128d,from,to),linear));
            Hr(Fn<PutLong>(key,9)(key,TimeSpan.FromSeconds(period).Ticks));Hr(Fn<PutInt>(key,11)(key,1));
            var animation=Query(scalar,"464c4c2c-1caa-4061-9b40-e13fde1503ca");Hr(Fn<Start>(composition,9)(composition,property,animation));started=true;
            var source=Query(composition,"0bb3784c-346b-4a7c-966b-7310966553d5");Hr(Fn<GetController>(source,6)(source,property,out var controller));if(controller==0)throw new InvalidOperationException("No scalar pulse controller.");references.Add(controller);
            Hr(Fn<Clear>(controller,12)(controller));Hr(Fn<PutFloat>(controller,9)(controller,(float)RuntimeCompat.Clamp(phase,0,1)));Hr(Fn<Clear>(controller,13)(controller));
        }
        catch{Dispose();throw;}
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        try
        {
            if(started)
            {
                Hr(Fn<Stop>(composition,10)(composition,property));started=false;
                var id=new Guid("0bb3784c-346b-4a7c-966b-7310966553d5");Hr(RuntimeCompat.QueryInterface(composition,id,out var source));
                try{Hr(Fn<GetController>(source,6)(source,property,out var remaining));ControllerAttachedAfterStop=remaining!=0;if(remaining!=0)Marshal.Release(remaining);}
                finally{Marshal.Release(source);}
            }
        }
        finally
        {
            for(var index=references.Count-1;index>=0;index--)Marshal.Release(references[index]);references.Clear();
            if(property!=0){WindowsDeleteString(property);property=0;}composition=0;
        }
    }
    private nint Query(nint pointer,string name){var id=new Guid(name);Hr(RuntimeCompat.QueryInterface(pointer,id,out var result));references.Add(result);return result;}
    private static T Fn<T>(nint pointer,int slot)where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(pointer),slot*IntPtr.Size));
    private static void Hr(int value)=>Marshal.ThrowExceptionForHR(value);
    [DllImport("combase.dll",CharSet=CharSet.Unicode)]private static extern int WindowsCreateString(string text,int length,out nint value);
    [DllImport("combase.dll")]private static extern int WindowsDeleteString(nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int CreateValue(nint self,out nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int InsertKeyFrame(nint self,float progress,float value,nint easing);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int PutLong(nint self,long value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int PutInt(nint self,int value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int PutFloat(nint self,float value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int Clear(nint self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int Start(nint self,nint property,nint animation);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int Stop(nint self,nint property);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int GetController(nint self,nint property,out nint controller);
}
