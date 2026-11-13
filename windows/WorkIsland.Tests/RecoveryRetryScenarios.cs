using System.Text.Json;
using WorkIsland.Core;

internal static class RecoveryRetryScenarios
{
    internal static void Run(string root,Action<string,Action> test)
    {
        void Require(bool value,string message){if(!value)throw new Exception(message);}
        void Blocked(Action save)
        {try{save();}catch(IOException){return;}throw new Exception("Expected actual sharing violation");}
        (string File,byte[] Bad,byte[] Good,AtomicStore Store,AppState State) Fixture(string name)
        {
            var dir=Path.Combine(root,"recovery-retry",name);Directory.CreateDirectory(dir);var file=Path.Combine(dir,"state.json");
            var state=new AppState{Settings=new(){Paused=true,GoalSeconds=14400,Rules=[]}};
            state.Sessions.Add(new(){ApplicationIdentifier="retained.exe",ApplicationName="Retained",DayKey="2026-10-02",StartedAt=DateTimeOffset.Parse("2026-10-02T10:00:00Z"),EndedAt=DateTimeOffset.Parse("2026-10-02T10:01:00Z"),EffectiveSeconds=60});
            var good=JsonSerializer.SerializeToUtf8Bytes(state,AtomicStore.JsonOptions);var bad=System.Text.Encoding.UTF8.GetBytes("damaged primary version A");
            System.IO.File.WriteAllBytes(file,bad);System.IO.File.WriteAllBytes(file+".bak",good);
            var store=new AtomicStore(file);return(file,bad,good,store,store.Load());
        }
        string[] Copies(string file)=>Directory.GetFiles(Path.GetDirectoryName(file)!,Path.GetFileName(file)+".corrupt-*");
        void Recovered((string File,byte[] Bad,byte[] Good,AtomicStore Store,AppState State) f)
        {
            f.Store.Save(f.State);var loaded=new AtomicStore(f.File).Load();
            Require(loaded.Settings.Paused&&loaded.Settings.GoalSeconds==14400&&loaded.Sessions.Single().Id==f.State.Sessions.Single().Id&&loaded.Sessions.Single().EffectiveSeconds==60,"Recovered data changed");
            Require(System.IO.File.ReadAllBytes(f.File+".bak").SequenceEqual(f.Good),"Recovery replaced the known-good backup");
        }
        test("recovery recreates a primary moved away after loading its good backup",()=>
        {
            var f=Fixture("missing-after-load");var moved=Path.Combine(Path.GetDirectoryName(f.File)!,"retained-original.bin");
            System.IO.File.Move(f.File,moved);Recovered(f);
            Require(Copies(f.File).Length==0&&System.IO.File.ReadAllBytes(moved).SequenceEqual(f.Bad),"Recovery changed moved source evidence");
        });
        test("recovery recreates a primary moved away after a rejected save and keeps prior evidence",()=>
        {
            var f=Fixture("missing-after-retry");using(var held=new FileStream(f.File,FileMode.Open,FileAccess.Read,FileShare.Read))Blocked(()=>f.Store.Save(f.State));
            var copy=Copies(f.File).Single();var moved=Path.Combine(Path.GetDirectoryName(f.File)!,"retained-original.bin");System.IO.File.Move(f.File,moved);Recovered(f);
            Require(Copies(f.File).Length==1&&System.IO.File.ReadAllBytes(copy).SequenceEqual(f.Bad)&&System.IO.File.ReadAllBytes(moved).SequenceEqual(f.Bad),"Recovery discarded earlier source evidence");
        });
        test("recovery retry reuses one unchanged corrupt snapshot under a real primary lock",()=>
        {
            var f=Fixture("same");
            using(var held=new FileStream(f.File,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                for(var i=0;i<6;i++)Blocked(()=>f.Store.Save(f.State));
                Require(Copies(f.File).Length==1,"Repeated rejected saves created redundant corrupt snapshots");
                Require(System.IO.File.ReadAllBytes(f.File).SequenceEqual(f.Bad)&&System.IO.File.ReadAllBytes(f.File+".bak").SequenceEqual(f.Good),"Rejected saves changed source data");
            }
            Recovered(f);Require(Copies(f.File).Length==1&&System.IO.File.ReadAllBytes(Copies(f.File).Single()).SequenceEqual(f.Bad),"Successful retry duplicated or changed retained source");
        });
        test("recovery retry preserves each changed corrupt primary version once",()=>
        {
            var f=Fixture("changed");using(var held=new FileStream(f.File,FileMode.Open,FileAccess.Read,FileShare.Read))Blocked(()=>f.Store.Save(f.State));
            var second=System.Text.Encoding.UTF8.GetBytes("damaged primary version B");System.IO.File.WriteAllBytes(f.File,second);
            using(var held=new FileStream(f.File,FileMode.Open,FileAccess.Read,FileShare.Read)){Blocked(()=>f.Store.Save(f.State));Blocked(()=>f.Store.Save(f.State));}
            var copies=Copies(f.File);Require(copies.Length==2&&copies.Any(p=>System.IO.File.ReadAllBytes(p).SequenceEqual(f.Bad))&&copies.Any(p=>System.IO.File.ReadAllBytes(p).SequenceEqual(second)),"Changed or unchanged source snapshots not handled distinctly");
            Recovered(f);Require(Copies(f.File).Length==2,"Successful recovery duplicated the last source version");
        });
        test("recovery retry recreates a moved snapshot without discarding the moved evidence",()=>
        {
            var f=Fixture("moved");using(var held=new FileStream(f.File,FileMode.Open,FileAccess.Read,FileShare.Read))Blocked(()=>f.Store.Save(f.State));
            var dir=Path.Combine(Path.GetDirectoryName(f.File)!,"kept");Directory.CreateDirectory(dir);var kept=Path.Combine(dir,"original.bin");System.IO.File.Move(Copies(f.File).Single(),kept);
            using(var held=new FileStream(f.File,FileMode.Open,FileAccess.Read,FileShare.Read)){Blocked(()=>f.Store.Save(f.State));Blocked(()=>f.Store.Save(f.State));}
            Require(Copies(f.File).Length==1&&System.IO.File.ReadAllBytes(kept).SequenceEqual(f.Bad),"Missing cached evidence was trusted or retries duplicated its replacement");Recovered(f);
        });
        test("recovery retry verifies cached snapshot bytes before reuse",()=>
        {
            var f=Fixture("modified");using(var held=new FileStream(f.File,FileMode.Open,FileAccess.Read,FileShare.Read))Blocked(()=>f.Store.Save(f.State));
            System.IO.File.WriteAllText(Copies(f.File).Single(),"modified owned test snapshot");
            using(var held=new FileStream(f.File,FileMode.Open,FileAccess.Read,FileShare.Read)){Blocked(()=>f.Store.Save(f.State));Blocked(()=>f.Store.Save(f.State));}
            Require(Copies(f.File).Length==2&&Copies(f.File).Count(p=>System.IO.File.ReadAllBytes(p).SequenceEqual(f.Bad))==1,"Tampered snapshot was trusted or valid replacement repeatedly duplicated");Recovered(f);
        });
        test("unreadable cached evidence gets a fresh copy and cannot prevent safe recovery",()=>
        {
            var f=Fixture("locked-copy");using(var held=new FileStream(f.File,FileMode.Open,FileAccess.Read,FileShare.Read))Blocked(()=>f.Store.Save(f.State));
            var retained=Copies(f.File).Single();using(var held=new FileStream(retained,FileMode.Open,FileAccess.Read,FileShare.None))Recovered(f);
            Require(Copies(f.File).Length==2&&Copies(f.File).All(p=>System.IO.File.ReadAllBytes(p).SequenceEqual(f.Bad)),"Unreadable evidence was trusted or lost");
        });
    }
}
