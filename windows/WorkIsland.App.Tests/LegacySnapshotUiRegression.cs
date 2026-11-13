using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static async Task RunLegacySnapshotUi(IslandWindow initial,string output,Action<string,bool,object?> check)
    {
        initial.Hide();var observations=new List<object>();
        foreach(var origin in new[]{"primary","backup-only","recovered"})
        {
            var directory=Path.Combine(output,origin);Directory.CreateDirectory(directory);var path=Path.Combine(directory,"state.json");
            var source=origin=="primary"?path:path+".bak";var state=new AppState{SchemaVersion=1,Settings=new(){Paused=true,DisplayTitle="Legacy retained",HasCompletedOnboarding=true,Rules=[]}};
            var start=new DateTimeOffset(2026,10,2,12,0,0,TimeSpan.Zero);state.Sessions.Add(new(){ApplicationIdentifier="legacy.exe",ApplicationName="Legacy fixture",DayKey="2026-10-02",StartedAt=start,EndedAt=start.AddSeconds(42),EffectiveSeconds=42});
            var raw=JsonSerializer.SerializeToUtf8Bytes(state,AtomicStore.JsonOptions);File.WriteAllBytes(source,raw);if(origin=="recovered")File.WriteAllText(path,"Retain corrupt primary");
            var store=new AtomicStore(path);var loaded=store.Load();var moved=Path.GetFullPath(source+".moved");
            if(!moved.StartsWith(Path.GetFullPath(output)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Move outside isolated test output");
            File.Move(source,moved);
            var owned=new IslandWindow(store,loaded,false,null,startupRegistration:new IsolatedStartupRegistration(Path.Combine(directory,"startup-fixture.json"),"isolated"),recognitionSource:new RecognitionFixture(),applicationPicker:new ApplicationPickerFixture(),fullscreenSource:new FullscreenFixture());
            object? Call(string name,params object?[] args)=>typeof(IslandWindow).GetMethod(name,Flags|BindingFlags.DeclaredOnly)!.Invoke(owned,args);
            T Field<T>(string name)=>(T)typeof(IslandWindow).GetField(name,Flags)!.GetValue(owned)!;
            owned.ShowActivated=false;owned.Topmost=false;owned.Loaded+=(_,_)=>Field<DispatcherTimer>("timer").Stop();owned.Show();owned.Topmost=false;
            await Task.Delay(150);var saved=(bool)Call("Persist")!;
            bool OnDisk()
            {
                try{var data=JsonSerializer.Deserialize<AppState>(File.ReadAllBytes(path),AtomicStore.JsonOptions)!;return data.SchemaVersion==2&&data.Sessions.Single().Id==state.Sessions.Single().Id&&data.Sessions.Single().EffectiveSeconds==42;}catch(Exception error)when(error is IOException or JsonException){return false;}
            }
            var archives=Directory.GetFiles(directory,"state.json.pre-v2-*");var exact=archives.Length==1&&File.ReadAllBytes(archives[0]).SequenceEqual(raw);
            check(origin+": actual loaded WPF window saves after legacy source move",saved&&OnDisk(),new{saved,error=Field<string?>("lastSaveError")});
            check(origin+": actual first save archives loaded original bytes",exact,new{archives=archives.Length});
            check(origin+": loaded paused state and history stay intact",loaded.Settings.Paused&&loaded.Sessions.Single().Id==state.Sessions.Single().Id&&loaded.Sessions.Single().EffectiveSeconds==42,null);
            var prepared=(bool)Call("PrepareNormalExit")!;check(origin+": product normal-exit preparation succeeds without source restore",prepared,null);
            var restored=false;
            if(!prepared){File.Copy(moved,source);restored=true;Call("Persist");prepared=(bool)Call("PrepareNormalExit")!;}
            if(!prepared)throw new InvalidOperationException("Keep owned fixture: saved normal exit still refused");
            Call("CompleteNormalExit");await Task.Delay(50);
            observations.Add(new{origin,saved,exact,neededSourceRestore=restored,normalExitPrepared=prepared,windowClosed=!owned.IsVisible,originalSha256=Convert.ToHexString(SHA256.HashData(raw)),retainedMovedSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(moved)))});
        }
        File.WriteAllText(Path.Combine(output,"legacy-ui.json"),JsonSerializer.Serialize(new{observations,scope="Exact product WPF windows, schema1 data loaded by AtomicStore then own source moved before window startup. Product Persist/normal-exit methods and real files; no ordinary EXE startup, physical input, crash, or user data."},AtomicStore.JsonOptions));
    }
}
