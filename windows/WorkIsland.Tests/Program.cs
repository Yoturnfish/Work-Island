using System.Diagnostics;
using System.Text.Json;
using WorkIsland.Core;

var results = new List<object>();
var failures = 0;
void Test(string name, Action run)
{
    try { run(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failures++; results.Add(new { name, passed = false, error = error.ToString() }); Console.WriteLine("FAIL " + name + ": " + error.Message); }
}
void Assert(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Near(double actual, double expected) => Assert(Math.Abs(actual - expected) < 0.00001, $"{actual} != {expected}");
void Throws(Action action) { try { action(); } catch (Exception) { return; } throw new Exception("Expected failure"); }
var options = AtomicStore.JsonOptions;
RuntimeCompatibilityScenarios.Run(Test);
var cases = JsonSerializer.Deserialize<List<Fixture>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "activity-decision-cases.json")), options)!;
foreach (var fixture in cases) Test("shared vector: " + fixture.Name, () =>
{
    var actual = ActivityDecision.Evaluate(fixture.Input, fixture.ApplicationRules, fixture.WebsiteRules,
        fixture.SupportedBrowserIdentifiers, fixture.AwayThreshold, fixture.IsManuallyPaused);
    Assert(actual.State == fixture.Expected.State);
    if (actual.Target is { } target) { Assert(target.Identifier == fixture.Expected.Identifier); Assert(target.DisplayName == fixture.Expected.DisplayName); Assert(target.Source == fixture.Expected.Source); Assert(target.CategoryRawValue == fixture.Expected.CategoryRawValue); }
});
var start = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
var active = new ActivityInput("code.exe", "Code", 0);
AppState Fresh() => new() { Settings = new() { Rules = [new("code.exe", "Code")] } };
TrackingEngine Engine(AppState? state = null) => new(state ?? Fresh(), TimeZoneInfo.Utc);
Test("legacy model fallback remains 4h / 30 seconds", () => { var data = Fresh(); Near(data.Settings.GoalSeconds, 14400); Near(data.Settings.AwayThreshold, 30); });
Test("idle boundary and automatic resume", () =>
{
    var engine = Engine(); engine.Observe(start, active);
    engine.Observe(start.AddSeconds(1), active with { IdleSeconds = 29.999 }); Assert(engine.Current.State == "tracking");
    engine.Observe(start.AddSeconds(2), active with { IdleSeconds = 30 }); Assert(engine.Current.State == "away"); Near(engine.TodaySeconds(start), 2);
    engine.Observe(start.AddSeconds(3), active with { IdleSeconds = 31 }); Near(engine.TodaySeconds(start), 2);
    engine.Observe(start.AddSeconds(4), active); Assert(engine.Current.State == "tracking");
    engine.Observe(start.AddSeconds(5), active); Near(engine.TodaySeconds(start), 3);
});
Test("idle crossing credits only until threshold", () => { var e = Engine(); e.Observe(start, active); e.Observe(start.AddSeconds(2), active with { IdleSeconds = 31 }); Near(e.TodaySeconds(start), 1); });
Test("manual pause persists until explicitly resumed", () =>
{
    var e = Engine(); e.Observe(start, active); e.Data.Settings.Paused = true; e.Observe(start.AddSeconds(1), active);
    Assert(e.Current.State == "paused"); e.Observe(start.AddSeconds(2), active); Near(e.TodaySeconds(start), 1);
    e.Data.Settings.Paused = false; e.Observe(start.AddSeconds(3), active); e.Observe(start.AddSeconds(4), active); Near(e.TodaySeconds(start), 2);
});
Test("unknown app switches away and never earns away time", () => { var e = Engine(); e.Observe(start, active); e.Observe(start.AddSeconds(1), new("game.exe", "Game", 0)); Assert(e.Current.State == "away"); e.Observe(start.AddSeconds(2), active); Near(e.TodaySeconds(start), 1); });
Test("application names match case-insensitively on Windows", () => Assert(ActivityDecision.Evaluate(active with { FrontmostApplicationIdentifier = "CODE.EXE" }, Fresh().Settings.Rules, [], [], 30, false).State == "tracking"));
Test("sleep / delayed callbacks cannot inflate work", () => { var e = Engine(); e.Observe(start, active); e.Observe(start.AddHours(1), active); Near(e.TodaySeconds(start), 0); e.Observe(start.AddHours(1).AddSeconds(1), active); Near(e.TodaySeconds(start), 1); });
Test("clock moves backward without negative work", () => { var e = Engine(); e.Observe(start, active); e.Observe(start.AddSeconds(-5), active); Near(e.TodaySeconds(start), 0); });
Test("clock correction alone cannot earn work", () =>
{
    var e = Engine(); e.Observe(start, active, 100);
    e.Observe(start.AddSeconds(3), active, 101); Near(e.TodaySeconds(start), 0);
    e.Observe(start.AddSeconds(4), active, 102); Near(e.TodaySeconds(start), 1);
});
Test("small clock slew retains monotonic duration and coalesces records", () =>
{
    var e=Engine();e.Observe(start,active,100);
    e.Observe(start.AddSeconds(1.2),active,101);e.Observe(start.AddSeconds(2.4),active,102);
    Near(e.TodaySeconds(start),2);Assert(e.Data.Sessions.Count==1);
    Assert(e.Data.Sessions[0].StartedAt==start && e.Data.Sessions[0].EndedAt==start.AddSeconds(2.4));
});
Test("monotonic idle clipping does not count clock slew", () =>
{
    var e=Engine();e.Observe(start,active,100);e.Observe(start.AddSeconds(1.2),active with{IdleSeconds=30.5},101);
    Near(e.TodaySeconds(start),.5);Assert(e.Current.State=="away");
});
Test("monotonic interval crosses custom reset using wall dates", () =>
{
    var data=Fresh();data.Settings.DayResetMinutes=180;var e=Engine(data);
    var t=new DateTimeOffset(2026,10,3,2,59,59,TimeSpan.Zero);
    e.Observe(t,active,100);e.Observe(t.AddSeconds(2.4),active,102);
    Assert(data.Sessions.Count==2);Near(data.Sessions[0].EffectiveSeconds,2/2.4);Near(data.Sessions[1].EffectiveSeconds,2*1.4/2.4);
    Near(data.Sessions.Sum(s=>s.EffectiveSeconds),2);
});
Test("monotonic reset and sleep gaps never earn work", () =>
{
    var e=Engine();e.Observe(start,active,100);e.Observe(start.AddHours(1),active,3700);Near(e.TodaySeconds(start),0);
    e.Observe(start.AddHours(1).AddSeconds(1),active,3701);Near(e.TodaySeconds(start),1);
    e.ResetObservation();e.Observe(start.AddHours(2),active,7300);Near(e.TodaySeconds(start),1);
});
Test("invalid monotonic sample cannot mutate an observation", () =>
{
    var e=Engine();e.Observe(start,active,100);Throws(()=>e.Observe(start.AddSeconds(1),active,double.NaN));
    Assert(e.LastObservation==start);e.Observe(start.AddSeconds(1),active,101);Near(e.TodaySeconds(start),1);
});
Test("reset after wake requires fresh observation", () => { var e = Engine(); e.Observe(start, active); e.ResetObservation(); e.Observe(start.AddHours(2), active); Near(e.TodaySeconds(start), 0); });
Test("midnight interval splits exactly", () =>
{
    var e = Engine(); var t = new DateTimeOffset(2026, 10, 2, 23, 59, 59, TimeSpan.Zero);
    e.Observe(t, active); e.Observe(t.AddSeconds(2), active); Assert(e.Data.Sessions.Count == 2);
    Near(e.Data.Sessions[0].EffectiveSeconds, 1); Near(e.Data.Sessions[1].EffectiveSeconds, 1);
    Assert(e.Data.Sessions[0].DayKey == "2026-10-02" && e.Data.Sessions[1].DayKey == "2026-10-03");
});
Test("custom 03:00 reset splits without UTC-day substitution", () =>
{
    var data = Fresh(); data.Settings.DayResetMinutes = 180; var e = Engine(data); var t = new DateTimeOffset(2026, 10, 3, 2, 59, 59, TimeSpan.Zero);
    e.Observe(t, active); e.Observe(t.AddSeconds(2), active); Assert(data.Sessions.Select(s => s.DayKey).SequenceEqual(new[] { "2026-10-02", "2026-10-03" }));
});
Test("non-UTC local day", () => { var zone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time"); Assert(LogicalDay.Key(new(2026, 10, 2, 18, 0, 0, TimeSpan.Zero), zone, 0) == "2026-10-03"); });
Test("DST spring day is 23h", () =>
{
    var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); var t = new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.FromHours(-5)); Near((LogicalDay.NextBoundary(t, zone, 0) - t).TotalHours, 23);
});
Test("DST fall day is 25h", () =>
{
    var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); var t = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(-4)); Near((LogicalDay.NextBoundary(t, zone, 0) - t).TotalHours, 25);
});
Test("DST invalid reset advances to first valid minute", () =>
{
    var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); var t = new DateTimeOffset(2026, 3, 8, 1, 59, 59, TimeSpan.FromHours(-5));
    var boundary = LogicalDay.NextBoundary(t, zone, 150); Assert(TimeZoneInfo.ConvertTime(boundary, zone).Hour == 3);
});
Test("goal progress capped at 100 percent", () => { var data = Fresh(); data.Settings.GoalSeconds = 1; var e = Engine(data); e.Observe(start, active); e.Observe(start.AddSeconds(2), active); Near(e.Progress(start), 1); });
Test("DST fall-back cannot roll logical day backward after reset", () =>
{
    var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
    Assert(LogicalDay.Key(new(2026, 11, 1, 1, 10, 0, TimeSpan.FromHours(-5)), zone, 90) == "2026-11-01");
});
Test("DST spring invalid reset keeps previous day until resolved boundary", () =>
{
    var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
    Assert(LogicalDay.Key(new(2026, 3, 8, 1, 59, 0, TimeSpan.FromHours(-5)), zone, 150) == "2026-03-07");
    Assert(LogicalDay.Key(new(2026, 3, 8, 3, 0, 0, TimeSpan.FromHours(-4)), zone, 150) == "2026-03-08");
});
Test("invalid numeric settings rejected", () => { var data = Fresh(); data.Settings.GoalSeconds = double.NaN; Throws(data.Validate); });
Test("website host-only rules retain Swift semantics", () => { var rule = new WebsiteRule("a", "a", "", "example", true, true); Assert(ActivityDecision.WebsiteMatches("https://docs.example.com/path", rule)); Assert(!ActivityDecision.WebsiteMatches("https://notexample.com/example", rule)); });
var temp = Path.Combine(Path.GetTempPath(), "WorkIsland-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
Test("atomic persistence restart retains rules/pause/work and excludes downtime", () =>
{
    var file = Path.Combine(temp, "restart.json"); var e = Engine(); e.Observe(start, active); e.Observe(start.AddSeconds(2), active); e.Data.Settings.Paused = true;
    var store = new AtomicStore(file); store.Save(e.Data); var copy = store.Load(); var restarted = Engine(copy);
    restarted.Observe(start.AddHours(1), active); Near(restarted.TodaySeconds(start), 2); Assert(restarted.Current.State == "paused"); Assert(copy.Settings.Rules.Count == 1);
});
Test("corrupt primary recovers backup and preserves damaged bytes", () =>
{
    var file = Path.Combine(temp, "recovery.json"); var store = new AtomicStore(file); store.Save(Fresh()); store.Save(Fresh()); File.WriteAllText(file, "damaged");
    var loaded = store.Load(); Assert(store.RecoveryMessage is not null); store.Save(loaded); Assert(Directory.GetFiles(temp, "recovery.json.corrupt-*").Length == 1);
});
Test("corrupt primary without backup fails without overwriting", () => { var file = Path.Combine(temp, "bad.json"); File.WriteAllText(file, "broken"); Throws(() => new AtomicStore(file).Load()); Assert(File.ReadAllText(file) == "broken"); });
Test("failed replacement retains primary and retries without losing its backup",()=>
{
    var file=Path.Combine(temp,"locked-save.json");var store=new AtomicStore(file);var data=Fresh();data.Settings.DisplayTitle="Saved";store.Save(data);
    data.Settings.DisplayTitle="Pending";
    using(var locked=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))
    {
        Throws(()=>store.Save(data));Assert(new AtomicStore(file).Load().Settings.DisplayTitle=="Saved");
    }
    store.Save(data);Assert(new AtomicStore(file).Load().Settings.DisplayTitle=="Pending");
    Assert(new AtomicStore(file+".bak").Load().Settings.DisplayTitle=="Saved");
});
Test("invalid save cannot change a valid primary or leave a temp write",()=>
{
    var file=Path.Combine(temp,"rejected-save.json");var store=new AtomicStore(file);var data=Fresh();store.Save(data);var previous=File.ReadAllBytes(file);
    data.Settings.DayResetMinutes=1440;Throws(()=>store.Save(data));Assert(File.ReadAllBytes(file).SequenceEqual(previous));Assert(!File.Exists(file+".tmp"));
});
Test("unsupported schema does not silently downgrade", () => { var data = Fresh(); data.SchemaVersion = 99; Throws(() => new AtomicStore(Path.Combine(temp, "schema.json")).Save(data)); });
Test("newer primary schema never falls back to older backup", () =>
{
    var file = Path.Combine(temp, "newer.json"); var store = new AtomicStore(file); store.Save(Fresh()); store.Save(Fresh());
    File.WriteAllText(file, File.ReadAllText(file).Replace("\"schemaVersion\": 2", "\"schemaVersion\": 99"));
    Throws(() => store.Load()); Assert(File.ReadAllText(file).Contains("\"schemaVersion\": 99"));
});
Test("persisted history contains no browserURL field", () => { var file = Path.Combine(temp, "privacy.json"); new AtomicStore(file).Save(Fresh()); Assert(!File.ReadAllText(file).Contains("browserURL", StringComparison.OrdinalIgnoreCase)); });
Test("legacy settings acquire safe visual defaults without changing rules", () =>
{
    var data=JsonSerializer.Deserialize<AppState>("{\"schemaVersion\":1,\"settings\":{\"rules\":[{\"identifier\":\"code.exe\",\"displayName\":\"Code\",\"isEnabled\":true,\"categoryRawValue\":\"coding\"}]},\"sessions\":[]}",options)!;
    data.Validate();Assert(data.Settings.BackgroundStyle=="black"&&data.Settings.BackgroundOpacity==1&&!data.Settings.SuperReminderEnabled&&data.Settings.ColorProfiles.Count==0);
    Assert(data.Settings.Rules.Single().Identifier=="code.exe");
});
Test("visual preferences and independent profile rules survive atomic restart", () =>
{
    var data=Fresh();data.Settings.BackgroundStyle="glass";data.Settings.BackgroundOpacity=.6;data.Settings.Language="en";
    data.Settings.ColorProfiles=[new(){Accent="green",Rules=[new("green.exe","Green")]},new(){Accent="blue",Title="Blue",ProgressStyle="waveform",Rules=[new("blue.exe","Blue")]}];
    var file=Path.Combine(temp,"visual-preferences.json");new AtomicStore(file).Save(data);var copy=new AtomicStore(file).Load();
    Assert(copy.Settings.BackgroundStyle=="glass"&&copy.Settings.BackgroundOpacity==.6&&copy.Settings.Language=="en");
    Assert(copy.Settings.ColorProfiles[0].Rules[0].Identifier=="green.exe"&&copy.Settings.ColorProfiles[1].Rules[0].Identifier=="blue.exe");
    Assert(copy.Settings.Rules.Single().Identifier=="code.exe"&&copy.Sessions.Count==0);
});
Test("invalid opacity and duplicate profile IDs are rejected", () =>
{
    var data=Fresh();data.Settings.BackgroundOpacity=double.NaN;Throws(data.Validate);
    data.Settings.BackgroundOpacity=1;var duplicate=Guid.NewGuid().ToString("D");data.Settings.ColorProfiles=[new(){Id=duplicate,Accent="green"},new(){Id=duplicate,Accent="green"}];Throws(data.Validate);
});
Test("v1 migration preserves active edits, other profiles and history without rewriting before save",()=>
{
    var file=Path.Combine(temp,"v1-migration.json");
    var legacy=Fresh();legacy.SchemaVersion=1;legacy.Settings.Accent="blue";legacy.Settings.DisplayTitle="Latest edit";
    legacy.Settings.ColorProfiles=[new(){Accent="green",Title="Original",Rules=[new("green.exe","Green")]},new(){Accent="blue",Title="Old snapshot",Rules=[]}];
    legacy.Sessions.Add(new(){DayKey="2026-10-02",StartedAt=start,EndedAt=start.AddSeconds(42),EffectiveSeconds=42,ApplicationIdentifier="old.exe",ApplicationName="Old"});
    var bytes=JsonSerializer.SerializeToUtf8Bytes(legacy,options);File.WriteAllBytes(file,bytes);
    var copy=new AtomicStore(file).Load();Assert(copy.SchemaVersion==2);Assert(File.ReadAllBytes(file).SequenceEqual(bytes));
    Assert(ColorProfilesService.Active(copy.Settings)!.Title=="Latest edit" && copy.Settings.Rules.Single().Identifier=="code.exe");
    Assert(copy.Settings.ColorProfiles.Single(p=>p.IsDefault).Rules.Single().Identifier=="green.exe");
    Assert(copy.Sessions.Single().EffectiveSeconds==42 && copy.Sessions[0].Id==legacy.Sessions[0].Id);
    var migratedStore=new AtomicStore(file);copy=migratedStore.Load();migratedStore.Save(copy);Assert(File.ReadAllBytes(file+".bak").SequenceEqual(bytes));
    migratedStore.Save(copy);var preserved=Directory.GetFiles(temp,"v1-migration.json.pre-v2-*").Single();Assert(File.ReadAllBytes(preserved).SequenceEqual(bytes));
});
Test("new theme uses empty rules instead of cloning default applications",()=>
{
    var data=Fresh();ColorProfilesService.Initialize(data.Settings);var profile=ColorProfilesService.Create(data.Settings,"blue","New");
    Assert(profile.Rules.Count==0 && data.Settings.Rules.Count==0 && data.Settings.DisplayTitle=="New");
    var e=Engine(data);e.Observe(start,active);Assert(e.Current.State=="away");
});
Test("same-color profiles retain independent IDs, rules, names and progress styles",()=>
{
    var data=Fresh();var one=ColorProfilesService.Create(data.Settings,"blue","One");data.Settings.Rules.Add(new("one.exe","One"));data.Settings.ProgressStyle="waveform";
    var two=ColorProfilesService.Create(data.Settings,"blue","Two");data.Settings.Rules.Add(new("two.exe","Two"));
    ColorProfilesService.Activate(data.Settings,one.Id);Assert(data.Settings.Rules.Single().Identifier=="one.exe" && data.Settings.DisplayTitle=="One" && data.Settings.ProgressStyle=="waveform");
    ColorProfilesService.Activate(data.Settings,two.Id);Assert(data.Settings.Rules.Single().Identifier=="two.exe" && data.Settings.DisplayTitle=="Two" && data.Settings.ProgressStyle=="matrix");data.Validate();
});
Test("rename trims and limits title without changing rules or history",()=>
{
    var data=Fresh();var profile=ColorProfilesService.Create(data.Settings,"pink","Name");data.Settings.Rules.Add(new("own.exe","Own"));
    ColorProfilesService.Rename(data.Settings,profile.Id,"  "+new string('字',65)+"  ");Assert(data.Settings.DisplayTitle==new string('字',60));Assert(data.Settings.Rules.Single().Identifier=="own.exe");
});
Test("Unicode titles keep complete emoji and survive atomic state reload",()=>
{
    var index=0;
    foreach(var item in new[]{
        (input:new string('a',59)+"😀",expected:new string('a',59)),
        (input:new string('a',58)+"😀",expected:new string('a',58)+"😀"),
        (input:"  剪辑🎬  ",expected:"剪辑🎬")})
    {
        var data=Fresh();var profile=ColorProfilesService.Create(data.Settings,"blue",item.input);
        Assert(data.Settings.DisplayTitle==item.expected);
        data.Settings.Rules.Add(new("unicode-editor.exe","Unicode editor"));
        data.Sessions.Add(new(){DayKey="2026-10-02",StartedAt=start,EndedAt=start.AddSeconds(9),EffectiveSeconds=9});
        ColorProfilesService.Rename(data.Settings,profile.Id,item.input);ColorProfilesService.SnapshotActive(data.Settings);
        var store=new AtomicStore(Path.Combine(temp,"unicode-title-"+(index++)+".json"));store.Save(data);var copy=store.Load();
        Assert(copy.Settings.DisplayTitle==item.expected && ColorProfilesService.Active(copy.Settings)!.Title==item.expected
            && copy.Settings.Rules.Single().Identifier=="unicode-editor.exe" && copy.Sessions.Sum(s=>s.EffectiveSeconds)==9);
    }
});
Test("deleting active theme stops matching and never falls back on restart",()=>
{
    var data=Fresh();var profile=ColorProfilesService.Create(data.Settings,"blue","Delete");data.Settings.Rules.Add(new("code.exe","Code"));ColorProfilesService.SnapshotActive(data.Settings);
    data.Sessions.Add(new(){DayKey="2026-10-02",StartedAt=start,EndedAt=start.AddSeconds(9),EffectiveSeconds=9});
    Assert(ColorProfilesService.Delete(data.Settings,profile.Id));Assert(data.Settings.Rules.Count==0 && data.Settings.ActiveProfileId==profile.Id);
    var file=Path.Combine(temp,"deleted-theme.json");new AtomicStore(file).Save(data);var copy=new AtomicStore(file).Load();
    var engine=Engine(copy);engine.Observe(start,active);Assert(engine.Current.State=="away");Near(copy.Sessions.Sum(s=>s.EffectiveSeconds),9);
    Assert(copy.Settings.ColorProfiles.Single(p=>p.IsDefault).Rules.Single().Identifier=="code.exe");
});
Test("missing theme cannot use stale top-level rules",()=>
{
    var data=Fresh();data.Settings.ActiveProfileId=Guid.NewGuid().ToString("D");var e=Engine(data);e.Observe(start,active);Assert(e.Current.State=="away");
    var file=Path.Combine(temp,"missing-theme.json");new AtomicStore(file).Save(data);Assert(new AtomicStore(file).Load().Settings.Rules.Count==0);
});
Test("default profile survives delete and inactive deletion preserves current rules",()=>
{
    var data=Fresh();ColorProfilesService.Initialize(data.Settings);var original=ColorProfilesService.Active(data.Settings)!;
    Assert(!ColorProfilesService.Delete(data.Settings,original.Id));var other=ColorProfilesService.Create(data.Settings,"orange","Soft pink");
    ColorProfilesService.Activate(data.Settings,original.Id);Assert(ColorProfilesService.Delete(data.Settings,other.Id));Assert(data.Settings.Rules.Single().Identifier=="code.exe");data.Validate();
});
Test("v2 profile IDs and orange slot survive atomic restart",()=>
{
    var data=Fresh();var profile=ColorProfilesService.Create(data.Settings,"orange","Soft pink");ColorProfilesService.SnapshotActive(data.Settings);
    var file=Path.Combine(temp,"v2-profiles.json");new AtomicStore(file).Save(data);var copy=new AtomicStore(file).Load();
    Assert(copy.SchemaVersion==2 && copy.Settings.ActiveProfileId==profile.Id && ColorProfilesService.Active(copy.Settings)!.Accent=="orange");
});
Test("profile GUID case retains active rules and rename/delete identity",()=>
{
    var data=Fresh();var profile=ColorProfilesService.Create(data.Settings,"blue","Mixed case");data.Settings.Rules.Add(new("code.exe","Code"));
    data.Settings.ActiveProfileId=profile.Id.ToUpperInvariant();var file=Path.Combine(temp,"guid-case.json");new AtomicStore(file).Save(data);
    var copy=new AtomicStore(file).Load();Assert(copy.Settings.Rules.Single().Identifier=="code.exe" && ColorProfilesService.Active(copy.Settings) is not null);
    ColorProfilesService.Rename(copy.Settings,profile.Id.ToUpperInvariant(),"Renamed");Assert(copy.Settings.DisplayTitle=="Renamed");
    Assert(ColorProfilesService.Delete(copy.Settings,profile.Id.ToUpperInvariant()) && copy.Settings.Rules.Count==0);
});
Test("reminder waits exactly one away threshold", () => {Near(ReminderTiming.Frequency(29.999,30),0);Near(ReminderTiming.Frequency(30,30),2d/30);Near(ReminderTiming.Intensity(30,30),0);});
Test("reminder doubles rounds and caps at 1.5 Hz", () => {Near(ReminderTiming.Frequency(60,30),4d/30);Near(ReminderTiming.Frequency(120,30),16d/30);Near(ReminderTiming.Frequency(180,30),1.5);Near(ReminderTiming.Frequency(10000,30),1.5);});
Test("reminder phase stays continuous at acceleration boundary", () => {Assert(Math.Abs(ReminderTiming.Intensity(60-1e-5,30)-ReminderTiming.Intensity(60+1e-5,30))<1e-6);Near(ReminderTiming.Intensity(30.75,30),1);Near(ReminderTiming.Intensity(180.3,30),1);});
Test("reminder ignores invalid timing and never changes work", () => {Near(ReminderTiming.Frequency(double.NaN,30),0);Near(ReminderTiming.Frequency(100,0),0);Near(ReminderTiming.Intensity(100,double.PositiveInfinity),0);var e=Engine();e.Observe(start,active);e.Observe(start.AddSeconds(1),active);_ =ReminderTiming.Intensity(120,30);Near(e.TodaySeconds(start),1);});
Test("48h scenario with 48 restarts, 24h downtime and damaged-primary recovery",()=>
{
    var index=Array.IndexOf(args,"--stability-output");
    var root=index>=0 && index+1<args.Length?Path.GetFullPath(args[index+1]):Path.Combine(temp,"stability-scenario");
    StabilityScenarios.Run(root);
});
Test("Mac input source selects most recent mouse or keyboard",()=>
{
    Assert(RecognitionRules.UsesPointer(0,3));Assert(RecognitionRules.UsesPointer(2,2));
    Assert(!RecognitionRules.UsesPointer(3,0));Assert(!RecognitionRules.UsesPointer(null,0));
});
Test("automatic website stores host only without credentials path or tokens",()=>
{
    var rule=RecognitionRules.Website("https://someone:secret@Docs.Example.com/folder?token=secret#key",true)!;
    Assert(rule.DisplayName=="docs.example.com" && rule.PathContains=="docs.example.com" && rule.MatchesHostOnly==true);
    Assert(!JsonSerializer.Serialize(rule).Contains("secret"));Assert(RecognitionRules.Website("chrome://settings",true) is null);
    Assert(RecognitionRules.SafeAddress("https://a:b@example.com/path?q=private#token")=="https://example.com/path");
});
Test("host rules match only exact host and subdomains",()=>
{
    var rule=RecognitionRules.Website("example.com")!;
    Assert(ActivityDecision.WebsiteMatches("https://example.com/page",rule));
    Assert(ActivityDecision.WebsiteMatches("https://docs.example.com/page",rule));
    Assert(!ActivityDecision.WebsiteMatches("https://notexample.com/page",rule));
    Assert(!ActivityDecision.WebsiteMatches("https://evil.com/example.com",rule));
    Assert(!ActivityDecision.WebsiteMatches("file://example.com/page",rule));
});
Test("website rules flow through real tracking engine before browser app rule",()=>
{
    var state=Fresh();state.Settings.Rules.Add(new("msedge.exe","Edge"));state.Settings.Websites.Add(RecognitionRules.Website("example.com")!);
    var e=Engine(state);var input=new ActivityInput("code.exe","Code",0,"msedge.exe","Edge","https://example.com/");
    e.Observe(start,input);Assert(e.Current.Target?.Source=="website");e.Observe(start.AddSeconds(2),input);
    Assert(state.Sessions.Single().ApplicationIdentifier.StartsWith("web.keyword."));Near(e.TodaySeconds(start),2);
    state.Settings.Websites[0]=state.Settings.Websites[0] with{IsEnabled=false};e.Observe(start.AddSeconds(3),input);
    Assert(e.Current.Target?.Identifier=="msedge.exe");
});
Test("missing URL never invents website identity and pointer outranks focus",()=>
{
    var state=Fresh();state.Settings.Websites.Add(RecognitionRules.Website("example.com")!);var e=Engine(state);
    e.Observe(start,new("code.exe","Code",0,"msedge.exe","Edge",null));Assert(e.Current.State=="away");
    e.Observe(start.AddSeconds(1),new("code.exe","Code",0,"","Unknown",null));Assert(e.Current.State=="away");
    e.Observe(start.AddSeconds(2),new("code.exe","Code",0));Assert(e.Current.State=="tracking");
});
Test("website profile isolation persists atomically and deletion retains history",()=>
{
    var data=Fresh();ColorProfilesService.Initialize(data.Settings);var first=data.Settings.ActiveProfileId!;
    data.Settings.Websites.Add(RecognitionRules.Website("one.example.com")!);
    var second=ColorProfilesService.Create(data.Settings,"blue","Sites");Assert(data.Settings.Websites.Count==0);
    data.Settings.Websites.Add(RecognitionRules.Website("two.example.com")!);
    ColorProfilesService.Activate(data.Settings,first);Assert(data.Settings.Websites.Single().DisplayName=="one.example.com");
    ColorProfilesService.Activate(data.Settings,second.Id);
    var path=Path.Combine(temp,"website-profiles.json");new AtomicStore(path).Save(data);var restored=new AtomicStore(path).Load();
    Assert(restored.Settings.Websites.Single().DisplayName=="two.example.com");
    Assert(ColorProfilesService.Delete(restored.Settings,second.Id));Assert(restored.Settings.Websites.Count==0);restored.Validate();
});
Test("old state without websites remains readable and invalid website lists rejected",()=>
{
    var data=JsonSerializer.Deserialize<AppState>("{\"schemaVersion\":2,\"settings\":{},\"sessions\":[]}",options)!;
    Assert(data.Settings.Websites.Count==0);data.Validate();
    data.Settings.Websites=null!;Throws(data.Validate);
});
Test("unknown idle readings discard previous interval and resume fresh",()=>
{
    foreach(var idle in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})
    {
        var e=Engine();e.Observe(start,active,10);e.Observe(start.AddSeconds(1),active with{IdleSeconds=idle},11);
        Assert(e.Current.State=="away");Near(e.TodaySeconds(start),0);
        e.Observe(start.AddSeconds(2),active,12);Near(e.TodaySeconds(start),0);
        e.Observe(start.AddSeconds(3),active,13);Near(e.TodaySeconds(start),1);
    }
});
Test("display mode defaults safely for old state and rejects unsupported mode",()=>
{
    var old=JsonSerializer.Deserialize<AppState>("{\"schemaVersion\":2,\"settings\":{},\"sessions\":[]}",options)!;
    Assert(old.Settings.DisplayMode=="floating");old.Settings.DisplayMode="menuBar";old.Validate();
    var roundTrip=JsonSerializer.Deserialize<AppState>(JsonSerializer.Serialize(old,options),options)!;
    Assert(roundTrip.Settings.DisplayMode=="menuBar");old.Settings.DisplayMode="unsupported";Throws(old.Validate);
});
Test("missing primary restores the available backup without rewriting evidence during load",()=>
{
    var file=Path.Combine(temp,"missing-primary.json");var store=new AtomicStore(file);var data=Fresh();
    data.Settings.DisplayTitle="Recovered title";
    var record=new Session{ApplicationIdentifier="code.exe",ApplicationName="Code",DayKey="2026-10-02",StartedAt=start,EndedAt=start.AddHours(1),EffectiveSeconds=3600};data.Sessions.Add(record);
    store.Save(data);store.Save(data);var backupBytes=File.ReadAllBytes(file+".bak");File.Move(file,file+".removed-primary-kept");
    var recovered=new AtomicStore(file);var loaded=recovered.Load();
    Assert(loaded.Sessions.Any(s=>s.Id==record.Id&&s.EffectiveSeconds==3600)&&loaded.Settings.DisplayTitle=="Recovered title");
    Assert(!File.Exists(file)&&File.ReadAllBytes(file+".bak").SequenceEqual(backupBytes)&&recovered.RecoveryMessage is not null);
    recovered.Save(loaded);Assert(new AtomicStore(file).Load().Sessions.Any(s=>s.Id==record.Id)&&File.ReadAllBytes(file+".bak").SequenceEqual(backupBytes));
});
Test("missing primary with unreadable backup fails and preserves original bytes",()=>
{
    var file=Path.Combine(temp,"missing-primary-invalid-backup.json");File.WriteAllText(file+".bak","preserved damaged backup");
    Throws(()=>new AtomicStore(file).Load());Assert(!File.Exists(file)&&File.ReadAllText(file+".bak")=="preserved damaged backup");
});
Test("missing primary migrates legacy backup only in memory and preserves raw legacy bytes",()=>
{
    var file=Path.Combine(temp,"missing-primary-legacy.json");var legacy=Fresh();legacy.SchemaVersion=1;legacy.Settings.DisplayTitle="Legacy recovered";
    var bytes=JsonSerializer.SerializeToUtf8Bytes(legacy,options);File.WriteAllBytes(file+".bak",bytes);
    var store=new AtomicStore(file);var recovered=store.Load();Assert(recovered.SchemaVersion==2&&recovered.Settings.DisplayTitle=="Legacy recovered");
    Assert(!File.Exists(file)&&File.ReadAllBytes(file+".bak").SequenceEqual(bytes));
    store.Save(recovered);Assert(new AtomicStore(file).Load().Settings.DisplayTitle=="Legacy recovered");
    Assert(File.ReadAllBytes(file+".bak").SequenceEqual(bytes)&&Directory.GetFiles(temp,"missing-primary-legacy.json.pre-v2-*").Any(p=>File.ReadAllBytes(p).SequenceEqual(bytes)));
});
Test("missing primary with newer backup never creates an empty replacement",()=>
{
    var file=Path.Combine(temp,"missing-primary-newer.json");var data=Fresh();data.SchemaVersion=99;
    var bytes=JsonSerializer.SerializeToUtf8Bytes(data,options);File.WriteAllBytes(file+".bak",bytes);
    Throws(()=>new AtomicStore(file).Load());Assert(!File.Exists(file)&&File.ReadAllBytes(file+".bak").SequenceEqual(bytes));
});
Test("only a new directory begins onboarding with Mac 2h goal and system UI language",()=>
{
    var culture=System.Globalization.CultureInfo.CurrentUICulture;
    try
    {
        foreach(var language in new[]{"zh-CN","en-US","fr-FR"})
        {
            System.Globalization.CultureInfo.CurrentUICulture=System.Globalization.CultureInfo.GetCultureInfo(language);
            var file=Path.Combine(temp,"onboarding-new-"+language,"state.json");var data=new AtomicStore(file).Load();
            Assert(!data.Settings.HasCompletedOnboarding&&data.Settings.Language==(language.StartsWith("zh")?"zh":"en")&&!File.Exists(file));
            Assert(data.Settings.GoalSeconds==7200&&data.Settings.AwayThreshold==30);
        }
    }
    finally{System.Globalization.CultureInfo.CurrentUICulture=culture;}
});
Test("new 2h goal survives first save resume and explicit goal choice",()=>
{
    var file=Path.Combine(temp,"initial-goal-roundtrip","state.json");var store=new AtomicStore(file);var state=store.Load();
    Assert(state.Settings.GoalSeconds==7200&&!File.Exists(file));store.Save(state);
    var resumed=new AtomicStore(file).Load();Assert(resumed.Settings.GoalSeconds==7200&&!resumed.Settings.HasCompletedOnboarding);
    resumed.Settings.GoalSeconds=21600;store.Save(resumed);Assert(new AtomicStore(file).Load().Settings.GoalSeconds==21600);
});
Test("initial-goal change preserves existing primary backup and legacy omitted goals",()=>
{
    foreach(var schema in new[]{1,2})foreach(var backupOnly in new[]{false,true})foreach(var goal in new double?[]{14400,21600,5401,null})
    {
        var state=Fresh();state.SchemaVersion=schema;state.Settings.GoalSeconds=goal??14400;
        var node=System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(state,options))!;
        if(goal is null)node["settings"]!.AsObject().Remove("goalSeconds");
        var file=Path.Combine(temp,$"initial-goal-existing-{schema}-{backupOnly}-{goal?.ToString()??"omitted"}.json");var source=file+(backupOnly?".bak":"");
        var bytes=System.Text.Encoding.UTF8.GetBytes(node.ToJsonString());File.WriteAllBytes(source,bytes);
        var loaded=new AtomicStore(file).Load();Assert(loaded.Settings.GoalSeconds==(goal??14400)&&loaded.Settings.HasCompletedOnboarding);
        Assert(File.ReadAllBytes(source).SequenceEqual(bytes)&&(!backupOnly||!File.Exists(file)));
    }
});
Test("old primary and backup without onboarding metadata are existing installations",()=>
{
    foreach(var backupOnly in new[]{false,true})
    {
        var file=Path.Combine(temp,"onboarding-existing-"+backupOnly+".json");var node=System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Fresh(),options))!;node["settings"]!.AsObject().Remove("hasCompletedOnboarding");
        var original=node.ToJsonString();File.WriteAllText(file+(backupOnly?".bak":""),original);
        var data=new AtomicStore(file).Load();Assert(data.Settings.HasCompletedOnboarding&&File.ReadAllText(file+(backupOnly?".bak":""))==original);
    }
});
Test("incomplete onboarding resumes and completed onboarding remains saved",()=>
{
    var store=new AtomicStore(Path.Combine(temp,"onboarding-pending","state.json"));var data=store.Load();store.Save(data);Assert(!store.Load().Settings.HasCompletedOnboarding);
    data.Settings.HasCompletedOnboarding=true;store.Save(data);Assert(store.Load().Settings.HasCompletedOnboarding);
});
AppState StructureState(int schema)
{
    var data=Fresh();data.SchemaVersion=schema;
    data.Sessions.Add(new(){ApplicationIdentifier="safe.exe",ApplicationName="Safe history",DayKey="2026-10-02",StartedAt=start,EndedAt=start.AddMinutes(1),EffectiveSeconds=60});
    return data;
}
void InvalidDataOnly(Action action)
{
    try{action();}catch(InvalidDataException){return;}
    throw new Exception("Expected structural InvalidDataException");
}
foreach(var schema in new[]{1,2})
{
    Test($"schema {schema}: null historical application identifier rejected before storage",()=>
    {var data=StructureState(schema);data.Sessions[0].ApplicationIdentifier=null!;InvalidDataOnly(data.Validate);});
    Test($"schema {schema}: structural primary corruption restores backup and preserves raw evidence",()=>
    {
        var data=StructureState(schema);var good=JsonSerializer.SerializeToUtf8Bytes(data,options);
        data.Sessions[0].ApplicationIdentifier=null!;var bad=JsonSerializer.SerializeToUtf8Bytes(data,options);
        var path=Path.Combine(temp,$"structure-recovery-{schema}.json");File.WriteAllBytes(path, bad);File.WriteAllBytes(path+".bak",good);
        var store=new AtomicStore(path);var loaded=store.Load();Assert(store.RecoveryMessage is not null&&loaded.Sessions.Single().ApplicationIdentifier=="safe.exe");
        Assert(File.ReadAllBytes(path).SequenceEqual(bad)&&File.ReadAllBytes(path+".bak").SequenceEqual(good));
        store.Save(loaded);Assert(new AtomicStore(path).Load().Sessions.Single().Id==data.Sessions.Single().Id);
        Assert(File.ReadAllBytes(path+".bak").SequenceEqual(good));var corrupt=Directory.GetFiles(temp,$"structure-recovery-{schema}.json.corrupt-*").Single();Assert(File.ReadAllBytes(corrupt).SequenceEqual(bad));
    });
    Test($"schema {schema}: structural primary without backup refuses without overwriting",()=>
    {
        var data=StructureState(schema);data.Sessions[0].ApplicationIdentifier=null!;var bad=JsonSerializer.SerializeToUtf8Bytes(data,options);
        var path=Path.Combine(temp,$"structure-no-backup-{schema}.json");File.WriteAllBytes(path,bad);InvalidDataOnly(()=>new AtomicStore(path).Load());Assert(File.ReadAllBytes(path).SequenceEqual(bad)&&!File.Exists(path+".tmp"));
    });
    Test($"schema {schema}: missing primary with structural backup refuses empty replacement",()=>
    {
        var data=StructureState(schema);data.Sessions[0].ApplicationIdentifier=null!;var bad=JsonSerializer.SerializeToUtf8Bytes(data,options);
        var path=Path.Combine(temp,$"structure-backup-only-{schema}.json");File.WriteAllBytes(path+".bak",bad);InvalidDataOnly(()=>new AtomicStore(path).Load());Assert(!File.Exists(path)&&File.ReadAllBytes(path+".bak").SequenceEqual(bad));
    });
    Test($"schema {schema}: missing legacy application field retains its existing empty default",()=>
    {
        var data=StructureState(schema);var node=System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(data,options))!;node["sessions"]![0]!.AsObject().Remove("applicationIdentifier");
        var path=Path.Combine(temp,$"structure-missing-field-{schema}.json");File.WriteAllText(path,node.ToJsonString());var loaded=new AtomicStore(path).Load();Assert(loaded.Sessions.Single().ApplicationIdentifier==""&&loaded.Sessions.Single().EffectiveSeconds==60);
    });
}
Test("invalid in-memory historical identifier cannot change primary backup or temp",()=>
{
    var path=Path.Combine(temp,"structure-rejected-save.json");var store=new AtomicStore(path);var data=StructureState(2);store.Save(data);store.Save(data);
    var primary=File.ReadAllBytes(path);var backup=File.ReadAllBytes(path+".bak");data.Sessions[0].ApplicationIdentifier=null!;InvalidDataOnly(()=>store.Save(data));Assert(File.ReadAllBytes(path).SequenceEqual(primary)&&File.ReadAllBytes(path+".bak").SequenceEqual(backup)&&!File.Exists(path+".tmp"));
});
Test("single snapshot commit preserves exact JSON and skips unchanged disk writes",()=>
{
    var path=Path.Combine(temp,"snapshot-commit.json");var store=new AtomicStore(path);var data=Fresh();
    var snapshot=store.SaveIfChanged(data,null);Assert(File.ReadAllText(path)==snapshot);
    using(var locked=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.None))Assert(store.SaveIfChanged(data,snapshot)==snapshot);
    Assert(!File.Exists(path+".bak"));store.SaveIfChanged(data,snapshot,true);Assert(File.ReadAllText(path+".bak")==snapshot);
});
Test("failed snapshot commit does not acknowledge edits and retry preserves the old backup",()=>
{
    var path=Path.Combine(temp,"snapshot-retry.json");var store=new AtomicStore(path);var data=Fresh();var saved=store.SaveIfChanged(data,null);
    data.Settings.DisplayTitle="新的工作时间";
    using(var locked=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.Read))Throws(()=>store.SaveIfChanged(data,saved));
    Assert(File.ReadAllText(path)==saved);var next=store.SaveIfChanged(data,saved);Assert(next!=saved&&File.ReadAllText(path)==next&&File.ReadAllText(path+".bak")==saved);
});
Test("invalid snapshot state cannot alter disk even with a matching previous snapshot",()=>
{
    var path=Path.Combine(temp,"snapshot-invalid.json");var store=new AtomicStore(path);var data=Fresh();var saved=store.SaveIfChanged(data,null);data.Settings.GoalSeconds=double.NaN;
    Throws(()=>store.SaveIfChanged(data,saved));Assert(File.ReadAllText(path)==saved);
});
RecoveryRetryScenarios.Run(temp,Test);
LegacyMigrationScenarios.Run(temp,Test);
WebsiteAddressScenarios.Run(Test);
AutomaticWebsiteScenarios.Run(Test);
AddressPreviewScenarios.Run(Test);
var outputIndex = Array.IndexOf(args, "--output");
if (outputIndex >= 0 && outputIndex + 1 < args.Length) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[outputIndex + 1]))!); File.WriteAllText(args[outputIndex + 1], JsonSerializer.Serialize(new { passed = failures == 0, total = results.Count, failures, results, temporaryData = temp }, options)); }
Console.WriteLine($"{results.Count - failures}/{results.Count} passed");
return failures == 0 ? 0 : 1;

sealed record Expected(string State, string? Identifier, string? DisplayName, string? CategoryRawValue, string? Source);
sealed record Fixture(string Name, ActivityInput Input, List<ApplicationRule> ApplicationRules, List<WebsiteRule> WebsiteRules, List<string> SupportedBrowserIdentifiers, double AwayThreshold, bool IsManuallyPaused, Expected Expected);
