using System.Diagnostics;
using System.Security.Cryptography;

namespace WorkIsland.Core;

// Small API bridges keep one implementation of the product on .NET and the
// Windows-supplied CLR. The data schema and control protocol stay identical.
public static class RuntimeCompat
{
    public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    public static double Clamp(double value,double min,double max)
    {
        if(min>max)throw new ArgumentException("Minimum exceeds maximum.");
        return value<min?min:value>max?max:value;
    }
    public static int Clamp(int value,int min,int max)
    {
        if(min>max)throw new ArgumentException("Minimum exceeds maximum.");
        return value<min?min:value>max?max:value;
    }
    public static int ProcessId { get; } = CurrentProcessId();
    private static int CurrentProcessId(){using var process=Process.GetCurrentProcess();return process.Id;}
    public static string? ProcessPath { get {using var process=Process.GetCurrentProcess();return process.MainModule?.FileName;} }
    public static byte[] HashData(byte[] bytes){using var hash=SHA256.Create();return hash.ComputeHash(bytes);}
    public static byte[] HashData(Stream stream){using var hash=SHA256.Create();return hash.ComputeHash(stream);}
    public static string ToHexString(byte[] bytes)=>BitConverter.ToString(bytes).Replace("-","");
    public static TimeSpan GetElapsedTime(long start)=>TimeSpan.FromSeconds((Stopwatch.GetTimestamp()-start)/(double)Stopwatch.Frequency);
    public static TimeSpan GetElapsedTime(long start,long end)=>TimeSpan.FromSeconds((end-start)/(double)Stopwatch.Frequency);
    public static bool IsWindowsVersionAtLeast(int major,int minor,int build)=>Environment.OSVersion.Platform==PlatformID.Win32NT&&Environment.OSVersion.Version>=new Version(major,minor,build);
    public static void ThrowIfDisposed(bool disposed,object owner){if(disposed)throw new ObjectDisposedException(owner.GetType().FullName);}
    public static byte[] Sha1(byte[] bytes){using var hash=SHA1.Create();return hash.ComputeHash(bytes);}
    public static int QueryInterface(IntPtr pointer,Guid iid,out IntPtr result)
    {
#if NETFRAMEWORK
        return System.Runtime.InteropServices.Marshal.QueryInterface(pointer,ref iid,out result);
#else
        return System.Runtime.InteropServices.Marshal.QueryInterface(pointer,in iid,out result);
#endif
    }
    public static byte[] GuidBigEndianBytes(Guid value){var bytes=value.ToByteArray();SwapGuidByteOrder(bytes);return bytes;}
    public static Guid GuidFromBigEndianBytes(byte[] value){var bytes=(byte[])value.Clone();SwapGuidByteOrder(bytes);return new Guid(bytes);}
    private static void SwapGuidByteOrder(byte[] bytes)
    {
        Array.Reverse(bytes,0,4);Array.Reverse(bytes,4,2);Array.Reverse(bytes,6,2);
    }
    public static void MoveFile(string source,string destination,bool overwrite)
    {
        if(overwrite&&File.Exists(destination))File.Replace(source,destination,null);
        else File.Move(source,destination);
    }
    public static string TrimEndingDirectorySeparator(string path)
    {
        var rootLength=Path.GetPathRoot(path)?.Length??0;
        var end=path.Length;
        while(end>rootLength&&(path[end-1]==Path.DirectorySeparatorChar||path[end-1]==Path.AltDirectorySeparatorChar))end--;
        return end==path.Length?path:path.Substring(0,end);
    }
#if NETFRAMEWORK
    public static bool Contains(this string value,string text,StringComparison comparison)=>value.IndexOf(text,comparison)>=0;
    public static bool Contains(this string value,char text)=>value.IndexOf(text)>=0;
    public static bool StartsWith(this string value,char text)=>value.Length>0&&value[0]==text;
    public static bool EndsWith(this string value,char text)=>value.Length>0&&value[value.Length-1]==text;
    public static string[] Split(this string value,char separator,StringSplitOptions options)=>value.Split(new[]{separator},options);
    public static IOrderedEnumerable<T> Order<T>(this IEnumerable<T> values)=>values.OrderBy(value=>value);
    public static void Deconstruct<TKey,TValue>(this KeyValuePair<TKey,TValue> pair,out TKey key,out TValue value){key=pair.Key;value=pair.Value;}
    public static TValue? GetValueOrDefault<TKey,TValue>(this IReadOnlyDictionary<TKey,TValue> values,TKey key)=>values.TryGetValue(key,out var value)?value:default;
    public static IEnumerable<T> TakeLast<T>(this IEnumerable<T> values,int count)
    {
        if(count<=0)yield break;
        var queue=new Queue<T>();
        foreach(var value in values){if(queue.Count==count)queue.Dequeue();queue.Enqueue(value);}
        foreach(var value in queue)yield return value;
    }
    public static void Write(this Stream stream,byte[] bytes)=>stream.Write(bytes,0,bytes.Length);
    public static Task WriteAsync(this Stream stream,byte[] bytes,CancellationToken token=default)=>stream.WriteAsync(bytes,0,bytes.Length,token);
    public static async Task ReadExactlyAsync(this Stream stream,byte[] bytes,CancellationToken token)
    {
        var offset=0;
        while(offset<bytes.Length)
        {
            var read=await stream.ReadAsync(bytes,offset,bytes.Length-offset,token).ConfigureAwait(false);
            if(read==0)throw new EndOfStreamException();
            offset+=read;
        }
    }
    public static async Task WaitForExitAsync(this Process process,CancellationToken token=default)
    {
        var done=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler exited=(_,_)=>done.TrySetResult(true);
        process.EnableRaisingEvents=true;process.Exited+=exited;
        try
        {
            using(token.Register(()=>done.TrySetCanceled()))
            {
                if(process.HasExited)done.TrySetResult(true);
                await done.Task.ConfigureAwait(false);
            }
        }
        finally{process.Exited-=exited;}
    }
#endif
}
