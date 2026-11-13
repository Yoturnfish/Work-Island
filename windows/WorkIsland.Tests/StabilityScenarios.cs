using System.Text.Json;
using WorkIsland.Core;

internal static class StabilityScenarios
{
    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        var dataRoot=Path.Combine(root,"run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dataRoot);
        var file=Path.Combine(dataRoot,"state.json");
        var store=new AtomicStore(file);
        var state=new AppState { Settings=new() { DayResetMinutes=180,
            Rules=[new("code.exe","Code"),new("blender.exe","Blender")], DisplayTitle="稳定性隔离数据" } };
        var engine=new TrackingEngine(state,TimeZoneInfo.Utc);
        var origin=new DateTimeOffset(2026,10,2,12,0,0,TimeSpan.Zero);
        var seconds=0d;var observations=0;var restarts=0;var writes=0;
        var expected=0d;
        void Observe(ActivityInput input) { engine.Observe(origin.AddSeconds(seconds),input,seconds);observations++; }
        void Advance(int count,ActivityInput input)
        { for(var n=0;n<count;n++){seconds++;Observe(input);} }
        void Verify()
        {
            state.Validate();
            if(Math.Abs(state.Sessions.Sum(s=>s.EffectiveSeconds)-expected)>1e-6)
                throw new Exception("48h scenario credited an incorrect duration");
        }
        for(var hour=0;hour<48;hour++)
        {
            var input=new ActivityInput(hour%2==0?"code.exe":"blender.exe","Editor",0);
            Observe(input);Advance(1800,input);expected+=1800;
            Verify();store.Save(state);writes++;
            state.Settings.Paused=true;Observe(input);Advance(600,input);
            state.Settings.Paused=false;Observe(input);Advance(900,input);expected+=900;
            Observe(input with{IdleSeconds=30});Advance(300,input with{IdleSeconds=330});
            Verify();store.Save(state);writes++;
            // An abandoned temp write is never promoted over the valid primary.
            File.WriteAllText(file+".tmp","interrupted write");
            state=new AtomicStore(file).Load();engine=new(state,TimeZoneInfo.Utc);restarts++;
            Verify();
        }
        var beforeGap=expected;
        var lastInput=new ActivityInput("code.exe","Code",0);
        Observe(lastInput);seconds+=24*3600;Observe(lastInput);
        Verify(); // 24h of downtime contributes nothing.
        Advance(1,lastInput);expected++;Verify();store.Save(state);writes++;
        var backup=new AtomicStore(file+".bak").Load();
        var backupTotal=backup.Sessions.Sum(s=>s.EffectiveSeconds);
        File.WriteAllText(file,"broken-primary-from-isolated-test");
        store=new(file);var recovered=store.Load();
        if(store.RecoveryMessage is null || recovered.Sessions.Sum(s=>s.EffectiveSeconds)!=backupTotal)
            throw new Exception("Recovery did not retain the previous complete checkpoint");
        store.Save(recovered);
        var preserved=Directory.GetFiles(dataRoot,"state.json.corrupt-*").Single();
        if(File.ReadAllText(preserved)!="broken-primary-from-isolated-test")throw new Exception("Damaged bytes not retained");
        File.WriteAllText(Path.Combine(root,"stability-scenario.json"),JsonSerializer.Serialize(new
        {
            passed=true, method="Accelerated deterministic core scenario; not a physical 72h runtime test",
            observedHours=48,downtimeHours=24,observations,restarts,writes,
            expectedTrackedSeconds=expected,actualTrackedSeconds=state.Sessions.Sum(s=>s.EffectiveSeconds),
            workingSecondsBeforeGap=beforeGap, sessions=state.Sessions.Count,
            logicalDays=state.Sessions.Select(s=>s.DayKey).Distinct().ToArray(),
            backupRecoveredSeconds=backupTotal,damagedPrimaryPreserved=preserved,
            durationSource="monotonic observations",dateSource="wall-clock UTC with 03:00 reset"
        },AtomicStore.JsonOptions));
    }
}
