using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WorkIsland.Core;

if(args.Length!=1)return 2;
var output=Path.GetFullPath(args[0]);
if(Directory.Exists(output))throw new IOException("Preserve prior evidence");
Directory.CreateDirectory(output);
var rows=new List<object>();var failures=0;
foreach(var scenario in new[]{"missing-before-load","moved-after-recovery-load","moved-after-rejected-save"})
{
    var directory=Path.Combine(output,scenario);Directory.CreateDirectory(directory);
    var file=Path.Combine(directory,"state.json");
    var source=new AppState{Settings=new(){Paused=true,GoalSeconds=5401,Rules=[new("retained.exe","Retained")],AwayThreshold=30.25}};
    source.Sessions.Add(new(){ApplicationIdentifier="retained.exe",ApplicationName="Retained",DayKey="2026-10-02",StartedAt=DateTimeOffset.Parse("2026-10-02T10:00:00Z"),EndedAt=DateTimeOffset.Parse("2026-10-02T10:01:00Z"),EffectiveSeconds=60});
    var good=JsonSerializer.SerializeToUtf8Bytes(source,AtomicStore.JsonOptions);var bad=Encoding.UTF8.GetBytes("synthetic damaged primary");
    File.WriteAllBytes(file+".bak",good);
    if(scenario!="missing-before-load")File.WriteAllBytes(file,bad);
    var store=new AtomicStore(file);var state=store.Load();
    string? rejectedSave=null;
    if(scenario=="moved-after-rejected-save")
    {
        using var locked=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);
        try{store.Save(state);throw new Exception("Expected a real sharing violation");}
        catch(IOException e){rejectedSave=e.GetType().Name;}
    }
    if(scenario!="missing-before-load")File.Move(file,Path.Combine(directory,"retained-bad-primary.bin"));
    state.Settings.GoalSeconds=7201; // A real pending edit must also survive recovery.
    Exception? error=null;
    try{store.Save(state);}catch(Exception e){error=e;}
    var loaded=File.Exists(file)?new AtomicStore(file).Load():null;
    var copies=Directory.GetFiles(directory,"state.json.corrupt-*");
    var backupUnchanged=File.ReadAllBytes(file+".bak").AsSpan().SequenceEqual(good);
    var retained=Path.Combine(directory,"retained-bad-primary.bin");
    var evidenceUnchanged=(!File.Exists(retained)||File.ReadAllBytes(retained).AsSpan().SequenceEqual(bad))&&copies.All(p=>File.ReadAllBytes(p).AsSpan().SequenceEqual(bad));
    var recovered=error is null&&loaded is not null&&loaded.Settings.GoalSeconds==7201&&loaded.Settings.AwayThreshold==30.25&&loaded.Settings.Paused&&loaded.Sessions.Single().Id==source.Sessions.Single().Id&&loaded.Sessions.Single().EffectiveSeconds==60&&loaded.Settings.Rules.Single().Identifier=="retained.exe";
    var passed=recovered&&backupUnchanged&&evidenceUnchanged;
    if(!passed)failures++;
    rows.Add(new{scenario,passed,recovered,backupUnchanged,evidenceUnchanged,corruptCopies=copies.Length,rejectedSave,error=error?.ToString()});
}
var core=typeof(AtomicStore).Assembly.Location;
var report=new{passed=failures==0,failures,core,coreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(core))),scenarios=rows,scope="Exact published Core with synthetic bad primary/good backup; only owned fixture files are moved, no deletion or product/UI/long-run claim."};
File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(new{passed=failures==0,failures,output}));
return failures==0?0:1;
