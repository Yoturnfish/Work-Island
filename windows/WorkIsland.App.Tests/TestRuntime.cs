using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal static class TestRuntime
{
    public static void AddArgument(ProcessStartInfo info,string value)
    {
#if NETFRAMEWORK
        var quoted=new StringBuilder("\"");var slashes=0;
        foreach(var ch in value)
        {
            if(ch=='\\'){slashes++;continue;}
            quoted.Append('\\',ch=='"'?slashes*2+1:slashes).Append(ch);slashes=0;
        }
        quoted.Append('\\',slashes*2).Append('"');
        info.Arguments+=(info.Arguments.Length==0?"":" ")+quoted;
#else
        info.ArgumentList.Add(value);
#endif
    }
    public static long GetTotalAllocatedBytes(bool precise=false)
    {
#if NETFRAMEWORK
        // Framework diagnostics report the current AppDomain's allocation
        // counter, not modern .NET's whole-process allocation counter.
        AppDomain.MonitoringIsEnabled=true;
        return AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
#else
        return GC.GetTotalAllocatedBytes(precise);
#endif
    }
    public static object GetUninitializedObject(Type type)
    {
#if NETFRAMEWORK
        return System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
#else
        return System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
#endif
    }
    public static double BitDecrement(double value)
    {
        if(double.IsNaN(value)||double.IsNegativeInfinity(value))return value;
        if(value==0)return -double.Epsilon;
        var bits=BitConverter.DoubleToInt64Bits(value);
        return BitConverter.Int64BitsToDouble(value>0?bits-1:bits+1);
    }
}

#if NETFRAMEWORK
internal sealed class TaskCompletionSource(TaskCreationOptions options=TaskCreationOptions.None)
{
    private readonly TaskCompletionSource<bool> source=new(options);
    public Task Task=>source.Task;
    public bool TrySetResult()=>source.TrySetResult(true);
    public void SetResult()=>source.SetResult(true);
    public bool TrySetException(Exception error)=>source.TrySetException(error);
    public void SetException(Exception error)=>source.SetException(error);
    public bool TrySetCanceled()=>source.TrySetCanceled();
}
internal static class FrameworkTestExtensions
{
    public static T CreateDelegate<T>(this MethodInfo method) where T:Delegate=>(T)method.CreateDelegate(typeof(T));
    public static async Task WaitAsync(this Task task,TimeSpan timeout)
    {
        using var stop=new CancellationTokenSource();
        if(await Task.WhenAny(task,Task.Delay(timeout,stop.Token)).ConfigureAwait(false)!=task)throw new TimeoutException();
        stop.Cancel();await task.ConfigureAwait(false);
    }
    public static async Task<T> WaitAsync<T>(this Task<T> task,TimeSpan timeout)
    {await ((Task)task).WaitAsync(timeout).ConfigureAwait(false);return await task.ConfigureAwait(false);}
    public static IEnumerable<(T First,U Second)> Zip<T,U>(this IEnumerable<T> first,IEnumerable<U> second)=>first.Zip(second,(a,b)=>(a,b));
    public static T MaxBy<T,TKey>(this IEnumerable<T> values,Func<T,TKey> key)
    {
        using var iterator=values.GetEnumerator();if(!iterator.MoveNext())throw new InvalidOperationException();
        var best=iterator.Current;var bestKey=key(best);
        while(iterator.MoveNext()){var current=iterator.Current;var nextKey=key(current);if(Comparer<TKey>.Default.Compare(nextKey,bestKey)>0){best=current;bestKey=nextKey;}}
        return best;
    }
    public static long ToInt64(this nint value)=>(long)value;
}
#endif
