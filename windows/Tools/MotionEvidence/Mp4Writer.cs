using System.Runtime.InteropServices;

// ABI slots checked against Microsoft's current mfobjects.h and mfreadwrite.h.
// Encoding follows https://learn.microsoft.com/windows/win32/medfound/tutorial--using-the-sink-writer-to-encode-video
internal sealed class Mp4Writer : IDisposable
{
    private nint writer;
    private uint stream;
    private bool comStarted, mfStarted, writing, finished;
    public Mp4Writer(string path, int width, int height, uint nominalRate)
    {
        nint outputType = 0, inputType = 0;
        try
        {
            var com = CoInitializeEx(0, 2);
            if (com < 0) Marshal.ThrowExceptionForHR(com);
            comStarted = true;
            Check(MFStartup(0x20070, 0)); mfStarted = true;
            Check(MFCreateSinkWriterFromURL(path, 0, 0, out writer));
            Check(MFCreateMediaType(out outputType));
            Configure(outputType, "34363248-0000-0010-8000-00aa00389b71", width, height, nominalRate);
            UInt(outputType, "20332624-fb0d-4d9e-bd0d-cbf6786c102e", 4_000_000);
            Check(Method<AddStream>(writer, 3)(writer, outputType, out stream));
            Check(MFCreateMediaType(out inputType));
            Configure(inputType, "00000016-0000-0010-8000-00aa00389b71", width, height, nominalRate);
            UInt(inputType, "644b4e48-1e02-4516-b0eb-c01ca9d49ac6", (uint)(width * 4));
            Check(Method<InputType>(writer, 4)(writer, stream, inputType, 0));
            Check(Method<Simple>(writer, 5)(writer)); writing = true;
        }
        catch { Dispose(); throw; }
        finally { if (inputType != 0) Marshal.Release(inputType); if (outputType != 0) Marshal.Release(outputType); }
    }
    private static void Configure(nint type, string subtype, int width, int height, uint rate)
    {
        GuidValue(type, "48eba18e-f8c9-4687-bf11-0a74c9f96a8f", "73646976-0000-0010-8000-00aa00389b71");
        GuidValue(type, "f7e34c9a-42e8-4714-b74b-cb29d72c35e5", subtype);
        UInt(type, "e2724bb8-e676-4806-b4b2-a8d6efb44ccd", 2);
        ULong(type, "1652c33d-d6b2-4012-b834-72030849a37d", ((ulong)(uint)width << 32) | (uint)height);
        ULong(type, "c459a2e8-3d2c-4e44-b132-fee5156c7bb0", ((ulong)rate << 32) | 1);
        ULong(type, "c6376a1e-8d0a-4027-be45-6d9a0ad39bb6", ((ulong)1 << 32) | 1);
    }
    public void Write(byte[] pixels, long timestamp, long duration)
    {
        nint buffer = 0, sample = 0;
        try
        {
            Check(MFCreateMemoryBuffer((uint)pixels.Length, out buffer));
            Check(Method<Lock>(buffer, 3)(buffer, out var memory, out _, out _));
            try { Marshal.Copy(pixels, 0, memory, pixels.Length); }
            finally { Check(Method<Simple>(buffer, 4)(buffer)); }
            Check(Method<Length>(buffer, 6)(buffer, (uint)pixels.Length));
            Check(MFCreateSample(out sample));
            Check(Method<Pointer>(sample, 42)(sample, buffer));
            Check(Method<Time>(sample, 36)(sample, timestamp));
            Check(Method<Time>(sample, 38)(sample, duration));
            Check(Method<WriteSample>(writer, 6)(writer, stream, sample));
        }
        finally { if (sample != 0) Marshal.Release(sample); if (buffer != 0) Marshal.Release(buffer); }
    }
    public void Finish()
    {
        if (!writing || finished) return;
        Check(Method<Simple>(writer, 11)(writer)); finished = true;
    }
    public void Dispose()
    {
        try { if (writer != 0 && writing && !finished) Finish(); }
        finally
        {
            if (writer != 0) { Marshal.Release(writer); writer = 0; }
            if (mfStarted) { MFShutdown(); mfStarted = false; }
            if (comStarted) { CoUninitialize(); comStarted = false; }
        }
    }
    private static void Check(int result) => Marshal.ThrowExceptionForHR(result);
    private static T Method<T>(nint value, int slot) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value), slot * nint.Size));
    private static void UInt(nint type, string key, uint value) { var id = new Guid(key); Check(Method<SetUInt>(type, 21)(type, ref id, value)); }
    private static void ULong(nint type, string key, ulong value) { var id = new Guid(key); Check(Method<SetULong>(type, 22)(type, ref id, value)); }
    private static void GuidValue(nint type, string key, string value) { var id = new Guid(key); var content = new Guid(value); Check(Method<SetGuid>(type, 24)(type, ref id, ref content)); }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Simple(nint self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int AddStream(nint self, nint type, out uint index);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int InputType(nint self, uint index, nint type, nint parameters);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int WriteSample(nint self, uint index, nint sample);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetUInt(nint self, ref Guid key, uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetULong(nint self, ref Guid key, ulong value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetGuid(nint self, ref Guid key, ref Guid value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Lock(nint self, out nint bytes, out uint maximum, out uint current);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Length(nint self, uint length);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Pointer(nint self, nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Time(nint self, long value);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("mfplat.dll")] private static extern int MFStartup(uint version, uint flags);
    [DllImport("mfplat.dll")] private static extern int MFShutdown();
    [DllImport("mfplat.dll")] private static extern int MFCreateMediaType(out nint type);
    [DllImport("mfplat.dll")] private static extern int MFCreateMemoryBuffer(uint length, out nint buffer);
    [DllImport("mfplat.dll")] private static extern int MFCreateSample(out nint sample);
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int MFCreateSinkWriterFromURL(string path, nint byteStream, nint attributes, out nint writer);
}
