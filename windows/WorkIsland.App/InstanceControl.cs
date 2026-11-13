using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WorkIsland.Core;

namespace WorkIsland.App;

public sealed record InstanceIdentity(int ProcessId,string StartedUtcTicks,string DataDirectory,string Version,string Executable);
public sealed record StatusAnchor(int X,int Y,int Width,int Height);
public sealed record ForegroundStatus(bool Enabled,bool Active,bool Failed,string? Failure,int Captures,double PhysicalOpacity,double PresentedOpacity,int HeldKeys,string Activity,long WindowHandle);
public sealed record InstanceSnapshot(bool Loaded,bool Visible,bool Expanded,string DisplayMode,bool MenuPanelOpen,bool StatusItemInstalled,StatusAnchor? StatusBounds,bool Paused,bool Suspended,bool FullscreenSuppressed,bool ExitPrepared,ForegroundStatus? Foreground=null,string? WpfRenderMode=null,bool SystemEffectsSubscribed=false,string? StartupProvider=null,bool RecoveryTrayVisible=false,bool StateTransitionActive=false);
public sealed record InstanceRequest(string Command,int ExpectedPid=0,string? ExpectedStartTicks=null);
public sealed record InstanceResponse(bool Success,string Status,InstanceIdentity? Instance=null,InstanceSnapshot? Window=null,string? Error=null);

public static class InstanceProtocol
{
    private const int MaximumPacket=16384;
    public static string Normalize(string directory)=>RuntimeCompat.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
    public static string PipeName(string directory)=>"WorkIsland-Control-"+RuntimeCompat.ToHexString(RuntimeCompat.HashData(Encoding.UTF8.GetBytes(Normalize(directory).ToUpperInvariant())))[..20];
    public static async Task WriteAsync<T>(Stream stream,T value,CancellationToken token)
    {
        var bytes=JsonSerializer.SerializeToUtf8Bytes(value,AtomicStore.JsonOptions);
        if(bytes.Length>MaximumPacket)throw new InvalidDataException("控制消息过长。");
        var prefix=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(prefix,bytes.Length);
        await stream.WriteAsync(prefix,token).ConfigureAwait(false);await stream.WriteAsync(bytes,token).ConfigureAwait(false);await stream.FlushAsync(token).ConfigureAwait(false);
    }
    public static async Task<T> ReadAsync<T>(Stream stream,CancellationToken token)
    {
        var prefix=new byte[4];await stream.ReadExactlyAsync(prefix,token).ConfigureAwait(false);
        var length=BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if(length<=0||length>MaximumPacket)throw new InvalidDataException("控制消息长度无效。");
        var bytes=new byte[length];await stream.ReadExactlyAsync(bytes,token).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(bytes,AtomicStore.JsonOptions)??throw new InvalidDataException("控制消息为空。");
    }
}

// Product-owned lifecycle endpoint. Only this instance's own normal-exit path
// can be invoked. No input injection, process termination or cross-user control.
public sealed class InstanceControlServer : IDisposable
{
    private readonly IslandWindow window;
    private readonly InstanceIdentity identity;
    private readonly CancellationTokenSource stop=new();
    private readonly Task worker;
    private bool disposed;
    public Task Completion=>worker;
    public string? Fault { get;private set; }
    public InstanceControlServer(IslandWindow window,string directory)
    {
        this.window=window;
        using var process=Process.GetCurrentProcess();
        identity=new(RuntimeCompat.ProcessId,process.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),InstanceProtocol.Normalize(directory),typeof(IslandWindow).Assembly.GetName().Version!.ToString(),RuntimeCompat.ProcessPath??"");
        worker=Task.Run(RunAsync);
    }
    private async Task RunAsync()
    {
        var token=stop.Token;
        NamedPipeServerStream pipe;
        try{pipe=InstancePipes.CreateServer(InstanceProtocol.PipeName(identity.DataDirectory));}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException){Fault=error.Message;return;}
        using(pipe)
        while(!token.IsCancellationRequested)
        {
            {
                var prepared=false;
                try
                {
                    await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    var request=await InstanceProtocol.ReadAsync<InstanceRequest>(pipe,timeout.Token).ConfigureAwait(false);
                    InstanceResponse response;
                    if(request.Command=="query")
                        response=new(true,"ready",identity,await window.Dispatcher.InvokeAsync(window.CaptureInstanceSnapshot));
                    else if(request.Command!="exit")response=new(false,"unsupported-command",identity);
                    else if(request.ExpectedPid!=identity.ProcessId||!string.Equals(request.ExpectedStartTicks,identity.StartedUtcTicks,StringComparison.Ordinal))
                        response=new(false,"identity-mismatch",identity);
                    else
                    {
                        prepared=await window.Dispatcher.InvokeAsync(window.PrepareNormalExit);
                        var snapshot=await window.Dispatcher.InvokeAsync(window.CaptureInstanceSnapshot);
                        var error=await window.Dispatcher.InvokeAsync(()=>window.NormalExitError);
                        response=new(prepared,prepared?"exit-prepared":"save-or-dialog-blocked",identity,snapshot,error);
                    }
                    await InstanceProtocol.WriteAsync(pipe,response,timeout.Token).ConfigureAwait(false);
                }
                catch(Exception error)when(error is IOException or JsonException or OperationCanceledException or InvalidDataException or InvalidOperationException)
                { if(token.IsCancellationRequested)return; }
                finally
                {
                    // A validated exit that has safely saved is completed even
                    // if its caller disconnects before consuming the reply.
                    if(prepared&&!window.Dispatcher.HasShutdownStarted)_=window.Dispatcher.BeginInvoke(window.CompleteNormalExit);
                    try{if(pipe.IsConnected)pipe.Disconnect();}catch(IOException){ }
                }
                if(prepared)return;
            }
        }
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;stop.Cancel();
        _=worker.ContinueWith(_=>stop.Dispose(),TaskScheduler.Default);
    }
}

public static class InstanceControlClient
{
    public static async Task<InstanceResponse> RequestAsync(string directory,InstanceRequest request,CancellationToken token=default)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(7));
        using var pipe=InstancePipes.CreateClient(InstanceProtocol.PipeName(directory));
        await pipe.ConnectAsync(1500,timeout.Token).ConfigureAwait(false);
        InstancePipes.VerifyServerOwner(pipe);
        await InstanceProtocol.WriteAsync(pipe,request,timeout.Token).ConfigureAwait(false);
        var response=await InstanceProtocol.ReadAsync<InstanceResponse>(pipe,timeout.Token).ConfigureAwait(false);
        if(response.Success&&response.Instance is null)return new(false,"missing-identity");
        if(response.Instance is {} identity&&!string.Equals(InstanceProtocol.Normalize(directory),identity.DataDirectory,StringComparison.OrdinalIgnoreCase))
            return new(false,"directory-mismatch");
        if(request.Command=="exit"&&response.Success&&(response.Instance!.ProcessId!=request.ExpectedPid||response.Instance.StartedUtcTicks!=request.ExpectedStartTicks))
            return new(false,"identity-mismatch");
        return response;
    }
    public static async Task<int> RunAsync(string directory,bool requestExit,int expectedPid,string? expectedStart)
    {
        try
        {
            using var process=requestExit?Process.GetProcessById(expectedPid):null;
            if(requestExit)
            {
                // Retain this exact OS process before asking it to exit. The
                // Framework Process adapter otherwise reopens by PID after
                // the reply, when a quick, successful exit can already be gone.
                _=process!.Handle;
                if(process!.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)!=expectedStart)
                {Console.WriteLine(JsonSerializer.Serialize(new InstanceResponse(false,"identity-mismatch"),AtomicStore.JsonOptions));return 3;}
            }
            {
                var response=await RequestAsync(directory,new(requestExit?"exit":"query",expectedPid,expectedStart)).ConfigureAwait(false);
                if(requestExit&&response.Success)
                {
                    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await process!.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                    var exitCode=process!.ExitCode;
                    response=response with{Success=exitCode==0,Status=exitCode==0?"exited":"abnormal-exit"};
                }
                Console.WriteLine(JsonSerializer.Serialize(response,AtomicStore.JsonOptions));return response.Success?0:3;
            }
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or TimeoutException or OperationCanceledException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {Console.WriteLine(JsonSerializer.Serialize(new InstanceResponse(false,"unavailable-or-not-exited",Error:error.Message),AtomicStore.JsonOptions));return 3;}
    }
}
