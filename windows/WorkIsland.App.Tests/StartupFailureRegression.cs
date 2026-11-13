using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private sealed class StartupFailureFixture:IStartupRegistration
    {
        public bool Enabled,WriteBeforeFailure,IgnoreWrites;
        public Exception? ReadFailure,WriteFailure,ReadFailureAfterWrite;
        public int Reads,Writes;
        public bool IsEnabled{get{Reads++;if(ReadFailure is {} failure)throw failure;return Enabled;}}
        public void SetEnabled(bool value)
        {
            Writes++;if(WriteBeforeFailure)Enabled=value;
            if(ReadFailureAfterWrite is {} denied)ReadFailure=denied;
            if(WriteFailure is {} failure)throw failure;
            if(!IgnoreWrites)Enabled=value;
        }
    }
    private static async Task RunStartupFailureChecks(string output,Action<string,bool,object?> check)
    {
        var snapshots=new List<object>();var serial=0;
        T Field<T>(IslandWindow window,string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(window)!;
        object? Call(IslandWindow window,string name,params object?[] values)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(window,values);
        async Task<IslandWindow> Create(StartupFailureFixture provider,string language="zh")
        {
            var folder=Path.Combine(output,"case-"+(++serial));Directory.CreateDirectory(folder);
            var window=new IslandWindow(new AtomicStore(Path.Combine(folder,"state.json")),new(){Settings=new(){Rules=[],Paused=true,Language=language,ReduceMotion=true}},false,null,startupRegistration:provider,recognitionSource:new RecognitionFixture(),applicationPicker:new ApplicationPickerFixture(),fullscreenSource:new FullscreenFixture());
            window.ShowActivated=false;window.Topmost=false;window.Loaded+=(_,_)=>Field<System.Windows.Threading.DispatcherTimer>(window,"timer").Stop();window.Show();await Task.Delay(60);Call(window,"ToggleExpanded",null,new RoutedEventArgs());Call(window,"OpenSettings",window,new RoutedEventArgs());return window;
        }
        void Close(IslandWindow window){typeof(IslandWindow).GetField("exiting",Flags)!.SetValue(window,true);window.Close();}
        object Snapshot(IslandWindow window,StartupFailureFixture provider)=>new{provider.Enabled,provider.Reads,provider.Writes,selected=Field<CheckBox>(window,"LoginCheck").IsChecked,editable=Field<CheckBox>(window,"LoginCheck").IsEnabled,message=Field<TextBlock>(window,"MessageLabel").Text};
        foreach(var failure in new Exception[]{new IOException("private-fixture-path io"),new UnauthorizedAccessException("private-fixture-path denied"),new SecurityException("private-fixture-path security"),new InvalidOperationException("private-fixture-path unavailable")})
        {
            var provider=new StartupFailureFixture();var window=await Create(provider);var login=Field<CheckBox>(window,"LoginCheck");
            try
            {
                provider.WriteFailure=failure;provider.ReadFailureAfterWrite=new IOException("private-fixture-path second read");var beforeReads=provider.Reads;var escaped=false;
                login.IsChecked=true;try{login.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));}catch{escaped=true;}
                var snapshot=Snapshot(window,provider);snapshots.Add(snapshot);var label=failure.GetType().Name;
                check(label+" write and status failure cannot escape UI handler",!escaped,snapshot);
                check(label+" unreadable result is disabled and indeterminate",!login.IsEnabled&&login.IsChecked is null,snapshot);
                check(label+" performs one bounded status read after write failure",provider.Reads-beforeReads==1,snapshot);
                check(label+" status failure has concise safe feedback",Field<TextBlock>(window,"MessageLabel").Text.Contains("无法读取登录项")&&!Field<TextBlock>(window,"MessageLabel").Text.Contains("private-fixture-path"),snapshot);
                provider.ReadFailure=null;provider.ReadFailureAfterWrite=null;provider.WriteFailure=null;provider.Enabled=true;var writes=provider.Writes;
                Call(window,"OpenSettings",window,new RoutedEventArgs());
                check(label+" reopening settings refreshes actual state without writing",login.IsEnabled&&login.IsChecked==true&&provider.Writes==writes,Snapshot(window,provider));
            }
            finally{Close(window);}
        }
        {
            var provider=new StartupFailureFixture();var window=await Create(provider,"en");var login=Field<CheckBox>(window,"LoginCheck");
            try
            {
                provider.ReadFailureAfterWrite=new UnauthorizedAccessException("private-fixture-path after successful write");var escaped=false;login.IsChecked=true;try{login.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));}catch{escaped=true;}
                check("committed registration with unreadable status stays unknown without exception",provider.Enabled&&!escaped&&!login.IsEnabled&&login.IsChecked is null,Snapshot(window,provider));
                check("English unreadable status uses localized safe feedback",Field<TextBlock>(window,"MessageLabel").Text.Contains("unavailable")&&!Field<TextBlock>(window,"MessageLabel").Text.Contains("private-fixture-path"),Snapshot(window,provider));
                provider.ReadFailure=null;provider.ReadFailureAfterWrite=null;Call(window,"OpenSettings",window,new RoutedEventArgs());
                check("recovered actual enabled state is not guessed as previous Off",login.IsEnabled&&login.IsChecked==true,Snapshot(window,provider));
                login.IsChecked=false;login.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                check("verified Off retry clears own stale status error",!provider.Enabled&&login.IsChecked==false&&login.IsEnabled&&!Field<TextBlock>(window,"MessageLabel").Text.Contains("unavailable"),Snapshot(window,provider));
                Field<TextBlock>(window,"MessageLabel").Text="Unrelated storage error";Field<TextBlock>(window,"MessageLabel").Visibility=Visibility.Visible;Call(window,"OpenSettings",window,new RoutedEventArgs());
                check("successful login refresh preserves unrelated storage feedback",Field<TextBlock>(window,"MessageLabel").Text=="Unrelated storage error",null);
            }
            finally{Close(window);}
        }
        {
            var provider=new StartupFailureFixture{ReadFailure=new IOException("private-fixture-path initial")};var window=await Create(provider);var login=Field<CheckBox>(window,"LoginCheck");
            try
            {
                check("initial status denial keeps usable window and unknown login state",window.IsVisible&&!login.IsEnabled&&login.IsChecked is null,Snapshot(window,provider));
                var engine=Field<TrackingEngine>(window,"engine");engine.Data.Settings.Language="en";Call(window,"ApplyLanguage");
                check("changing language translates existing login status feedback",Field<TextBlock>(window,"MessageLabel").Text.Contains("unavailable"),Snapshot(window,provider));
                provider.ReadFailure=null;Call(window,"OpenSettings",window,new RoutedEventArgs());
                check("initial read denial can recover by reopening settings",login.IsEnabled&&login.IsChecked==false&&!Field<TextBlock>(window,"MessageLabel").IsVisible,Snapshot(window,provider));
            }
            finally{Close(window);}
        }
        {
            var provider=new StartupFailureFixture{IgnoreWrites=true};var window=await Create(provider);var login=Field<CheckBox>(window,"LoginCheck");
            try
            {
                login.IsChecked=true;login.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                check("ignored registration cannot report successful enable",login.IsChecked==false&&Field<TextBlock>(window,"MessageLabel").Text.Contains("未应用"),Snapshot(window,provider));
                provider.IgnoreWrites=false;provider.WriteBeforeFailure=true;provider.WriteFailure=new IOException("private-fixture-path partial write");login.IsChecked=true;login.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                check("partial write failure presents verified actual state without claiming unchanged",provider.Enabled&&login.IsChecked==true&&Field<TextBlock>(window,"MessageLabel").Text.Contains("设置失败")&&!Field<TextBlock>(window,"MessageLabel").Text.Contains("未更改"),Snapshot(window,provider));
            }
            finally{Close(window);}
        }
        File.WriteAllText(Path.Combine(output,"startup-failure.json"),JsonSerializer.Serialize(new{productDllSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),snapshots,scope="Own actual IslandWindow controls with injected startup provider exceptions, partial/ignored writes and readable recovery. No Windows Run key writes, ACL changes, native input or real login acceptance."},AtomicStore.JsonOptions));
    }
}
