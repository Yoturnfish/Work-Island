using System.Runtime.InteropServices;

namespace WorkIsland.App;

// Windows.UI.Composition's documented host backdrop brush. Its visual opacity
// blends actual blurred background with the sharp desktop, independently of text.
// ABI GUIDs and slots are verified against Windows.UI.winmd by Tools/WinRtInspect.
internal sealed class HostBackdrop : IDisposable
{
    [ThreadStatic] private static nint dispatcherQueue;
    private readonly List<nint> references = [];
    private nint compositor, target, visual;
    private bool initialized;
    public HostBackdrop(nint window)
    {
        try
        {
            Marshal.ThrowExceptionForHR(RoInitialize(0)); initialized = true;
            EnsureDispatcherQueue();
            const string name = "Windows.UI.Composition.Compositor";
            Marshal.ThrowExceptionForHR(WindowsCreateString(name, name.Length, out var className));
            try
            {
                var iid = new Guid("00000035-0000-0000-c000-000000000046");
                Marshal.ThrowExceptionForHR(RoGetActivationFactory(className, ref iid, out var factory)); references.Add(factory);
                Marshal.ThrowExceptionForHR(Function<Create>(factory, 6)(factory, out compositor)); references.Add(compositor);
            }
            finally { WindowsDeleteString(className); }
            var compositor3 = Query(compositor, "c9dd8ef0-6eb1-4e3c-a658-675d9c64d4ab");
            Marshal.ThrowExceptionForHR(Function<Create>(compositor3, 6)(compositor3, out var backdrop)); references.Add(backdrop);
            var brush = Query(backdrop, "ab0d7608-30c0-40e9-b568-b60a6bd1fb46");
            var compositor1 = Query(compositor, "b403ca50-7f8c-4e83-985f-cc45060036d8");
            Marshal.ThrowExceptionForHR(Function<Create>(compositor1, 22)(compositor1, out var sprite)); references.Add(sprite);
            var spriteInterface = Query(sprite, "08e05581-1ad1-4f97-9757-402d76e4233b");
            Marshal.ThrowExceptionForHR(Function<PutObject>(spriteInterface, 7)(spriteInterface, brush));
            visual = Query(sprite, "117e202d-a859-4c89-873b-c2aa566788e3");
            var interop = Query(compositor, "29e691fa-4567-4dca-b319-d0f207eb6807");
            Marshal.ThrowExceptionForHR(Function<CreateTarget>(interop, 3)(interop, window, 0, out var desktopTarget)); references.Add(desktopTarget);
            target = Query(desktopTarget, "a1bea8ba-d726-4663-8129-6b5e7927ffa6");
            Marshal.ThrowExceptionForHR(Function<PutObject>(target, 7)(target, visual));
            SetBlend(.25);
        }
        catch { Dispose(); throw; }
    }
    public void SetBlend(double value) => Marshal.ThrowExceptionForHR(Function<PutFloat>(visual, 23)(visual, (float)Math.Clamp(value, 0, 1)));
    public void Resize(int width, int height) => Marshal.ThrowExceptionForHR(Function<PutVector>(visual, 36)(visual, new Vector { X = width, Y = height }));
    // One queue per UI thread, shared by the backdrop and native progress target.
    // Controllers live for the dispatcher lifetime, rather than individual HWNDs.
    internal static void EnsureDispatcherQueue()
    {
        if (dispatcherQueue == 0)
            Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(new QueueOptions { Size = 12, Thread = 2, Apartment = 2 }, out dispatcherQueue));
    }
    private nint Query(nint pointer, string guid)
    {
        var iid = new Guid(guid); Marshal.ThrowExceptionForHR(Marshal.QueryInterface(pointer, in iid, out var result)); references.Add(result); return result;
    }
    private static T Function<T>(nint pointer, int slot) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(pointer), slot * nint.Size));
    public void Dispose()
    {
        if (target != 0) { Function<PutObject>(target, 7)(target, 0); target = 0; }
        for (int i = references.Count - 1; i >= 0; i--) Marshal.Release(references[i]);
        references.Clear(); visual = compositor = 0;
        if (initialized) { RoUninitialize(); initialized = false; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct QueueOptions { public int Size, Thread, Apartment; }
    [StructLayout(LayoutKind.Sequential)] private struct Vector { public float X, Y; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Create(nint self, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PutObject(nint self, nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PutFloat(nint self, float value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PutVector(nint self, Vector value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateTarget(nint self, nint window, int topmost, out nint result);
    [DllImport("coremessaging.dll")] private static extern int CreateDispatcherQueueController(QueueOptions options, out nint controller);
    [DllImport("combase.dll")] private static extern int RoInitialize(uint type);
    [DllImport("combase.dll")] private static extern void RoUninitialize();
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string text, int length, out nint value);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint name, ref Guid iid, out nint factory);
}
