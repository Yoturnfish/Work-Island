using System.Text.Json;
using WorkIsland.Core;

internal static class LegacyMigrationScenarios
{
    public static void Run(string root,Action<string,Action> test)
    {
        static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
        static AppState Legacy(string title)
        {
            var state=new AppState{SchemaVersion=1,Settings=new(){DisplayTitle=title,Rules=[new("legacy-editor.exe","Legacy editor")]}};
            var started=new DateTimeOffset(2026,10,2,12,0,0,TimeSpan.Zero);
            state.Sessions.Add(new(){ApplicationIdentifier="legacy-editor.exe",ApplicationName="Retained history",DayKey="2026-10-02",StartedAt=started,EndedAt=started.AddSeconds(42),EffectiveSeconds=42});return state;
        }
        static byte[] Bytes(AppState state)=>JsonSerializer.SerializeToUtf8Bytes(state,AtomicStore.JsonOptions);
        string MoveOwned(string source)
        {
            var destination=Path.GetFullPath(source+".moved");
            Require(destination.StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Move target outside fixture directory");
            File.Move(source,destination);return destination;
        }
        foreach(var origin in new[]{"primary","backup-only","recovered"})foreach(var change in new[]{"moved","replaced"})
        {
            test($"legacy {origin} {change}: preserve exact loaded bytes and save migrated state",()=>
            {
                var directory=Path.Combine(root,"legacy-snapshot-"+origin+"-"+change);Directory.CreateDirectory(directory);
                var path=Path.Combine(directory,"state.json");var source=origin=="primary"?path:path+".bak";
                var old=Legacy("Original loaded name");var original=Bytes(old);File.WriteAllBytes(source,original);
                if(origin=="recovered")File.WriteAllText(path,"corrupt primary retained");
                var store=new AtomicStore(path);var data=store.Load();Require(data.SchemaVersion==2,"Load should migrate only in memory");
                Require(Directory.GetFiles(directory,"*.pre-v2-*").Length==0,"Load should not create a migration backup");
                string? moved=null;
                if(change=="moved")moved=MoveOwned(source);else File.WriteAllBytes(source,Bytes(Legacy("Replacement after load")));
                data.Settings.DisplayTitle="Migrated retained name";store.Save(data);
                var archive=Directory.GetFiles(directory,"state.json.pre-v2-*").Single();
                Require(File.ReadAllBytes(archive).SequenceEqual(original),"Migration archive differs from the bytes actually loaded");
                var resumed=new AtomicStore(path).Load();Require(resumed.SchemaVersion==2&&resumed.Settings.DisplayTitle=="Migrated retained name","Migrated save not committed");
                Require(resumed.Sessions.Single().Id==old.Sessions.Single().Id&&resumed.Sessions.Single().EffectiveSeconds==42,"History changed");
                if(moved is not null)Require(File.ReadAllBytes(moved).SequenceEqual(original),"Moved source changed");
                if(origin=="recovered")Require(Directory.GetFiles(directory,"state.json.corrupt-*").Any(p=>File.ReadAllText(p)=="corrupt primary retained"),"Corrupt primary evidence not retained");
                store.Save(data);Require(Directory.GetFiles(directory,"state.json.pre-v2-*").Length==1,"Retry duplicated successful migration archive");
            });
        }
        test("legacy failed commit after source move keeps one exact archive and retries",()=>
        {
            var directory=Path.Combine(root,"legacy-snapshot-retry");Directory.CreateDirectory(directory);var path=Path.Combine(directory,"state.json");
            var original=Bytes(Legacy("Retry migration"));File.WriteAllBytes(path,original);var store=new AtomicStore(path);var data=store.Load();MoveOwned(path);
            using(var locked=new FileStream(path+".tmp",FileMode.Create,FileAccess.ReadWrite,FileShare.None))
            {
                var refused=false;try{store.Save(data);}catch(IOException){refused=true;}
                Require(refused,"Locked temp unexpectedly accepted save");
                var archive=Directory.GetFiles(directory,"state.json.pre-v2-*").Single();Require(File.ReadAllBytes(archive).SequenceEqual(original),"Failed commit lost exact migration archive");
            }
            store.Save(data);Require(new AtomicStore(path).Load().Sessions.Single().EffectiveSeconds==42,"Retry lost loaded history");
            Require(Directory.GetFiles(directory,"state.json.pre-v2-*").Length==1,"Retry created another migration archive");
        });
        test("legacy repeated Load archives latest successfully loaded version",()=>
        {
            var directory=Path.Combine(root,"legacy-snapshot-reload");Directory.CreateDirectory(directory);var path=Path.Combine(directory,"state.json");var store=new AtomicStore(path);
            File.WriteAllBytes(path,Bytes(Legacy("First")));store.Load();var latest=Bytes(Legacy("Second"));File.WriteAllBytes(path,latest);var state=store.Load();store.Save(state);
            Require(File.ReadAllBytes(Directory.GetFiles(directory,"state.json.pre-v2-*").Single()).SequenceEqual(latest),"Reload retained obsolete bytes");
        });
        test("schema2 reload clears an earlier pending legacy archive",()=>
        {
            var directory=Path.Combine(root,"legacy-snapshot-reload-v2");Directory.CreateDirectory(directory);var path=Path.Combine(directory,"state.json");var store=new AtomicStore(path);
            File.WriteAllBytes(path,Bytes(Legacy("Legacy first")));store.Load();var current=Legacy("Current schema");current.SchemaVersion=2;ColorProfilesService.Initialize(current.Settings);File.WriteAllBytes(path,Bytes(current));
            var loaded=store.Load();store.Save(loaded);Require(Directory.GetFiles(directory,"state.json.pre-v2-*").Length==0,"Schema2 bytes were mislabeled as pre-v2 legacy");
        });
    }
}
