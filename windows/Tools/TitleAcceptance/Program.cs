using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorkIsland.App;
using WorkIsland.Core;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if(args.Length!=1 && !(args.Length==2 && args[1]=="--expect-fixed"))return 2;
        var expectFixed=args.Length==2;
        var output=Path.GetFullPath(args[0]);
        var root=Path.GetFullPath(Path.Combine(Environment.CurrentDirectory,"test-results"))+Path.DirectorySeparatorChar;
        if(!output.StartsWith(root,StringComparison.OrdinalIgnoreCase) || Directory.Exists(output))return 2;
        Directory.CreateDirectory(output);
        var application=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        IslandWindow? window=null;
        try
        {
            var state=new AppState();state.Settings.Rules=[];state.Settings.Topmost=false;
            ColorProfilesService.Initialize(state.Settings);
            var profile=ColorProfilesService.Create(state.Settings,"blue","initial");
            var input=new string('a',59)+char.ConvertFromUtf32(0x1F600);
            var store=new AtomicStore(Path.Combine(output,"state.json"));
            var startup=new IsolatedStartupRegistration(Path.Combine(output,"startup-fixture.json"),"isolated-only");
            window=new IslandWindow(store,state,false,null,startupRegistration:startup) {ShowActivated=false,Left=100,Top=100};
            window.Show();
            var editor=(TextBox)window.FindName("TitleEditor");
            editor.Text=input;
            var method=typeof(IslandWindow).GetMethod("TitleKeyDown",BindingFlags.Instance|BindingFlags.NonPublic)!;
            string? handlerError=null;
            try{method.Invoke(window,[editor,new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window)!,0,Key.Enter)]);}
            catch(TargetInvocationException error){handlerError=error.InnerException?.ToString() ?? error.ToString();}
            var inMemory=state.Settings.DisplayTitle;
            var reloaded=store.Load();
            var persisted=reloaded.Settings.DisplayTitle;
            var coreState=new AppState();ColorProfilesService.Initialize(coreState.Settings);
            var coreProfile=ColorProfilesService.Create(coreState.Settings,"blue","initial");
            ColorProfilesService.Rename(coreState.Settings,coreProfile.Id,input);
            var coreStore=new AtomicStore(Path.Combine(output,"core-name-state.json"));coreStore.Save(coreState);
            var coreReloaded=coreStore.Load().Settings.DisplayTitle;
            var mismatch=inMemory!=persisted;
            File.WriteAllText(Path.Combine(output,"title-probe.json"),JsonSerializer.Serialize(new
            {
                status=mismatch?"reproduced-title-persistence-mismatch":"title-persistence-exact",
                productRoundTripPassed=!mismatch,inputUtf16Length=input.Length,
                expectFixed,expectedFixedLength=59,
                inMemoryLength=inMemory.Length,persistedLength=persisted.Length,
                inMemoryLastCodeUnit=(int)inMemory[^1],persistedLastCodeUnit=(int)persisted[^1],
                coreNamingRoundTripPassed=coreState.Settings.DisplayTitle==coreReloaded,
                coreNamingLength=coreState.Settings.DisplayTitle.Length,handlerError,
                appSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(IslandWindow).Assembly.Location))),
                profileId=profile.Id,method="Published product TitleKeyDown Enter handler and AtomicStore, with synthetic 59 ASCII characters plus one emoji; no OS mouse or keyboard input",dailyDataUsed=false
            },AtomicStore.JsonOptions));
            return expectFixed ? (!mismatch && inMemory==new string('a',59) && handlerError is null ? 0 : 1) : (mismatch ? 0 : 1);
        }
        catch(Exception error){File.WriteAllText(Path.Combine(output,"probe-error.txt"),error.ToString());return 2;}
        finally
        {
            if(window is not null){typeof(IslandWindow).GetField("exiting",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,true);window.Close();}
            application.Shutdown();
        }
    }
}
