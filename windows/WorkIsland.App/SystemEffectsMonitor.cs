using WorkIsland.Core;
using System.Runtime.InteropServices;

namespace WorkIsland.App;

// IUISettings4 slots/GUID come from installed Windows.UI.winmd. The typed
// delegate IID and its IUnknown/Invoke layout are verified against Microsoft's
// windows.ui.viewmanagement.h, retained with this build's ABI evidence.
internal sealed class SystemEffectsMonitor:IDisposable
{
    private nint instance,settings,handler;
    private long token;
    private bool initialized,disposed;
    private readonly EffectsSink sink;
    internal bool Subscribed{get;private set;}
    internal int RemoveResult{get;private set;}
    internal int OwnedReferences=>(instance!=0?1:0)+(settings!=0?1:0)+(handler!=0?1:0);
    internal bool Enabled
    {
        get{RuntimeCompat.ThrowIfDisposed(disposed,this);Marshal.ThrowExceptionForHR(Fn<GetBoolean>(settings,6)(settings,out var enabled));return enabled!=0;}
    }
    internal SystemEffectsMonitor(Action changed)
    {
        sink=new(changed);
        try
        {
            Marshal.ThrowExceptionForHR(RoInitialize(0));initialized=true;
            const string name="Windows.UI.ViewManagement.UISettings";
            Marshal.ThrowExceptionForHR(WindowsCreateString(name,name.Length,out var text));
            try{Marshal.ThrowExceptionForHR(RoActivateInstance(text,out instance));}finally{WindowsDeleteString(text);}
            var id=new Guid("52bb3002-919b-4d6b-9b78-8dd66ff4b93b");Marshal.ThrowExceptionForHR(RuntimeCompat.QueryInterface(instance,id,out settings));
            handler=Marshal.GetComInterfaceForObject(sink,typeof(ISystemEffectsChanged));
            Marshal.ThrowExceptionForHR(Fn<AddEvent>(settings,7)(settings,handler,out token));Subscribed=true;
            _=Enabled;
        }
        catch{Dispose();throw;}
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;sink.Detach();
        try{if(Subscribed&&settings!=0)RemoveResult=Fn<RemoveEvent>(settings,8)(settings,token);}
        finally
        {
            Subscribed=false;token=0;
            if(handler!=0){Marshal.Release(handler);handler=0;}
            if(settings!=0){Marshal.Release(settings);settings=0;}
            if(instance!=0){Marshal.Release(instance);instance=0;}
            if(initialized){RoUninitialize();initialized=false;}
        }
    }
    [ComVisible(true),ClassInterface(ClassInterfaceType.None)]
    private sealed class EffectsSink(Action changed):ISystemEffectsChanged,ISystemEffectsAgile
    {
        private Action? callback=changed;
        internal void Detach()=>Interlocked.Exchange(ref callback,null);
        public int Invoke(nint sender,nint args)
        {
            try{Volatile.Read(ref callback)?.Invoke();return 0;}
            catch(Exception error){return error.HResult;}
        }
    }
    private static T Fn<T>(nint value,int slot)where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value),slot*IntPtr.Size));
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int GetBoolean(nint self,out byte value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int AddEvent(nint self,nint callback,out long token);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int RemoveEvent(nint self,long token);
    [DllImport("combase.dll")]private static extern int RoInitialize(uint type);
    [DllImport("combase.dll")]private static extern void RoUninitialize();
    [DllImport("combase.dll",CharSet=CharSet.Unicode)]private static extern int WindowsCreateString(string text,int length,out nint value);
    [DllImport("combase.dll")]private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")]private static extern int RoActivateInstance(nint name,out nint instance);
}

[ComVisible(true),Guid("2dbdba9d-20da-519d-9078-09f835bc5bc7"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ISystemEffectsChanged
{
    [PreserveSig]int Invoke(nint sender,nint args);
}

// Callback only posts to the owning Dispatcher; it never reads a COM object or
// touches WPF from the notifying thread, so the delegate is safe to call agilely.
[ComVisible(true),Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ISystemEffectsAgile{}
