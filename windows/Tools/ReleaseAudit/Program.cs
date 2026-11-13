using System.Diagnostics;
using System.Text.Json;
using WorkIsland.Core;

var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
var now=DateTimeOffset.Now;var state=new AppState{Settings=new(){HasCompletedOnboarding=true}};
ColorProfilesService.Initialize(state.Settings);
for(var i=0;i<3000;i++)state.Sessions.Add(new(){ApplicationIdentifier="audit.exe",ApplicationName="独立性能夹具",DayKey=now.ToString("yyyy-MM-dd"),StartedAt=now.AddMinutes(-1),EndedAt=now,EffectiveSeconds=1});
var beforeStore=new AtomicStore(Path.Combine(output,"before.json"));var afterStore=new AtomicStore(Path.Combine(output,"after.json"));
string? before=null,after=null;
void OldSave(){ColorProfilesService.SnapshotActive(state.Settings);var json=JsonSerializer.Serialize(state,AtomicStore.JsonOptions);if(json!=before){beforeStore.Save(state);before=json;}}
void NewSave(){after=afterStore.SaveIfChanged(state,after);}
OldSave();NewSave();
var rows=new List<object>();long oldBytes=0,newBytes=0;double oldMs=0,newMs=0;
void Sample(string kind,Action save,bool record)
{
    var allocated=GC.GetAllocatedBytesForCurrentThread();var at=Stopwatch.GetTimestamp();save();var ms=Stopwatch.GetElapsedTime(at).TotalMilliseconds;var bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
    if(!record)return;
    if(kind=="before"){oldBytes+=bytes;oldMs+=ms;}else{newBytes+=bytes;newMs+=ms;}
    rows.Add(new{kind,ms,bytes});
}
for(var i=0;i<12;i++)
{
    state.Sessions[^1].EffectiveSeconds+=.1;
    if(i%2==0){Sample("before",OldSave,i>=2);Sample("after",NewSave,i>=2);}else{Sample("after",NewSave,i>=2);Sample("before",OldSave,i>=2);}
}
var equal=File.ReadAllBytes(beforeStore.Path).SequenceEqual(File.ReadAllBytes(afterStore.Path));
var result=new{passed=equal&&newBytes<oldBytes,sessions=3000,samplesPerPath=10,jsonBytes=new FileInfo(afterStore.Path).Length,beforeAllocated=oldBytes,afterAllocated=newBytes,allocationReduction=1-newBytes/(double)oldBytes,beforeMs=oldMs,afterMs=newMs,identicalSavedBytes=equal,rows,scope="Alternating synchronous dirty saves using the old double-serialization application path versus SaveIfChanged; same validated state, WriteThrough, Flush(true), atomic replace and backup. Not whole-app CPU or disk lifetime guarantee."};
File.WriteAllText(Path.Combine(output,"save-performance.json"),JsonSerializer.Serialize(result,AtomicStore.JsonOptions));Console.WriteLine(JsonSerializer.Serialize(result with { }));return result.passed?0:1;
