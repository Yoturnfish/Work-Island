using System.Globalization;

namespace WorkIsland.Core;

public sealed class ColorProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("D");
    public bool IsDefault { get; set; }
    public string Accent { get; set; } = "green";
    public string Title { get; set; } = "";
    public string ProgressStyle { get; set; } = "matrix";
    public List<ApplicationRule> Rules { get; set; } = [];
    public List<WebsiteRule> Websites { get; set; } = [];
}
public sealed class Settings
{
    // Preserve the fallback used by older payloads that omitted this field.
    // AtomicStore chooses the Mac 2h default only when no primary/backup exists.
    public double GoalSeconds { get; set; } = 4 * 3600;
    public double AwayThreshold { get; set; } = 30;
    public int DayResetMinutes { get; set; }
    public bool Paused { get; set; }
    public bool Topmost { get; set; } = true;
    public double IslandScale { get; set; } = 0.82;
    public string ProgressStyle { get; set; } = "matrix";
    public string DurationStyle { get; set; } = "hoursMinutes";
    public string DisplayMode { get; set; } = "floating";
    public string DisplayTitle { get; set; } = "";
    public bool ReduceMotion { get; set; }
    public string BackgroundStyle { get; set; } = "black";
    public double BackgroundOpacity { get; set; } = 1;
    public string Language { get; set; } = "zh";
    // Older files without this key belong to an existing installation.
    public bool HasCompletedOnboarding { get; set; } = true;
    public string Accent { get; set; } = "green";
    public bool AwayTimerEnabled { get; set; }
    public bool SuperReminderEnabled { get; set; }
    public List<ColorProfile> ColorProfiles { get; set; } = [];
    public string? ActiveProfileId { get; set; }
    public double? WindowX { get; set; }
    public double? WindowY { get; set; }
    public List<WebsiteRule> Websites { get; set; } = [];
    public List<ApplicationRule> Rules { get; set; } = [
        new("Resolve.exe", "DaVinci Resolve", true, "editing"), new("Photoshop.exe", "Adobe Photoshop", true, "design"),
        new("blender.exe", "Blender", true, "threeD"), new("Fusion360.exe", "Fusion 360", true, "threeD"),
        new("notepad.exe", "记事本"), new("code.exe", "Visual Studio Code"),
        new("devenv.exe", "Visual Studio"), new("WINWORD.exe", "Word"),
        new("EXCEL.exe", "Excel"), new("POWERPNT.exe", "PowerPoint"), new("Obsidian.exe", "Obsidian")];
    public void Validate()
    {
        if (!RuntimeCompat.IsFinite(GoalSeconds) || GoalSeconds <= 0 || GoalSeconds > 24 * 3600
            || !RuntimeCompat.IsFinite(AwayThreshold) || AwayThreshold < 1 || AwayThreshold > 3600
            || DayResetMinutes < 0 || DayResetMinutes >= 1440
            || !RuntimeCompat.IsFinite(IslandScale) || IslandScale < 0.5 || IslandScale > 1
            || ProgressStyle is not ("matrix" or "waveform") || DurationStyle is not ("hoursMinutes" or "minutesSeconds") || DisplayMode is not ("floating" or "menuBar")
            || DisplayTitle is null || DisplayTitle.Length > 60
            || BackgroundStyle is not ("black" or "glass") || !RuntimeCompat.IsFinite(BackgroundOpacity) || BackgroundOpacity < .45 || BackgroundOpacity > 1
            || Language is not ("zh" or "en") || !ColorProfilesService.Accents.Contains(Accent)
            || ColorProfiles is null || ColorProfiles.Select(p => p?.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != ColorProfiles.Count
            || ColorProfiles.Count(p=>p?.IsDefault==true)>1 || ActiveProfileId is not null && !Guid.TryParse(ActiveProfileId,out _)
            || ColorProfiles.Any(p => p is null || !Guid.TryParse(p.Id,out _) || !ColorProfilesService.Accents.Contains(p.Accent) || p.IsDefault && p.Accent!="green"
                || p.Title is null || p.Title.Length > 60 || p.ProgressStyle is not ("matrix" or "waveform") || p.Rules is null
                || p.Rules.Any(r => r is null || string.IsNullOrWhiteSpace(r.Identifier) || string.IsNullOrWhiteSpace(r.DisplayName)) || !RecognitionRules.ValidWebsites(p.Websites))
            || !RecognitionRules.ValidWebsites(Websites)
            || Rules is null || Rules.Any(r => r is null || string.IsNullOrWhiteSpace(r.Identifier) || string.IsNullOrWhiteSpace(r.DisplayName))
            || WindowX is double x && !RuntimeCompat.IsFinite(x) || WindowY is double y && !RuntimeCompat.IsFinite(y))
            throw new InvalidDataException("设置文件包含无效值。");
    }
}
public sealed class Session
{
    public string Id { get; set; } = Guid.NewGuid().ToString("D");
    public string ApplicationIdentifier { get; set; } = "";
    public string ApplicationName { get; set; } = "";
    public string CategoryRawValue { get; set; } = "other";
    public string DayKey { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public double EffectiveSeconds { get; set; }
}
public sealed class AppState
{
    public int SchemaVersion { get; set; } = 2;
    public Settings Settings { get; set; } = new();
    public List<Session> Sessions { get; set; } = [];
    public void Validate()
    {
        if (SchemaVersion is not (1 or 2)) throw new NotSupportedException("不支持的数据版本。请使用对应版本的软件，原文件不会被改写。");
        if (Settings is null || Sessions is null) throw new InvalidDataException("数据结构不完整。");
        Settings.Validate();
        if(SchemaVersion==1 && Settings.ColorProfiles.Select(p=>p.Accent).Distinct().Count()!=Settings.ColorProfiles.Count)
            throw new InvalidDataException("旧数据包含重复颜色组合。");
        // A missing legacy field keeps Session's empty-string default, but an
        // explicit null cannot be a usable history grouping/editor key. Reject
        // it before the UI or migration so AtomicStore can recover safely.
        if (Sessions.Any(s => s is null || s.ApplicationIdentifier is null || !RuntimeCompat.IsFinite(s.EffectiveSeconds) || s.EffectiveSeconds < 0 || s.EndedAt < s.StartedAt
            || !DateOnly.TryParseExact(s.DayKey, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
            throw new InvalidDataException("计时记录包含无效值。");
    }
}

public static class LogicalDay
{
    public static string Key(DateTimeOffset instant, TimeZoneInfo zone, int resetMinutes)
    {
        var localDate = TimeZoneInfo.ConvertTime(instant, zone).Date;
        var boundary = ResolveBoundary(localDate.AddMinutes(resetMinutes), zone);
        var date = instant >= boundary ? localDate : localDate.AddDays(-1);
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
    public static DateTimeOffset NextBoundary(DateTimeOffset instant, TimeZoneInfo zone, int resetMinutes)
    {
        var boundary = TimeZoneInfo.ConvertTime(instant, zone).Date.AddMinutes(resetMinutes);
        var result = ResolveBoundary(boundary, zone);
        if (result <= instant) result = ResolveBoundary(boundary.AddDays(1), zone);
        return result;
    }
    private static DateTimeOffset ResolveBoundary(DateTime boundary, TimeZoneInfo zone)
    {
        while (zone.IsInvalidTime(boundary)) boundary = boundary.AddMinutes(1);
        // During fall-back use the earlier occurrence, then advance to the next day.
        var offset = zone.IsAmbiguousTime(boundary) ? zone.GetAmbiguousTimeOffsets(boundary).Max() : zone.GetUtcOffset(boundary);
        return new DateTimeOffset(boundary, offset);
    }
}

public sealed class TrackingEngine(AppState data, TimeZoneInfo? zone = null)
{
    public AppState Data { get; } = data;
    public TimeZoneInfo Zone { get; set; } = zone ?? TimeZoneInfo.Local;
    public Decision Current { get; private set; } = new(data.Settings.Paused?"paused":"away");
    public DateTimeOffset? LastObservation { get; private set; }
    private double? lastMonotonicSeconds;
    public double TodaySeconds(DateTimeOffset now) => Data.Sessions.Where(s => s.DayKey == LogicalDay.Key(now, Zone, Data.Settings.DayResetMinutes)).Sum(s => s.EffectiveSeconds);
    public double Progress(DateTimeOffset now) => RuntimeCompat.Clamp(TodaySeconds(now) / Data.Settings.GoalSeconds, 0, 1);

    public void Observe(DateTimeOffset now, ActivityInput input) => Observe(now, input, null);

    // Adapters supply a monotonic clock for elapsed work. Wall time still owns
    // record timestamps and logical-day boundaries. The two-argument overload
    // remains available for deterministic timestamp fixtures.
    public void Observe(DateTimeOffset now, ActivityInput input, double? monotonicSeconds)
    {
        if (monotonicSeconds is double clock && !RuntimeCompat.IsFinite(clock))
            throw new ArgumentOutOfRangeException(nameof(monotonicSeconds));
        // Credit only the previously observed tracking interval. Restart and sleep gaps earn nothing.
        if (LastObservation is DateTimeOffset previous && Current.Target is { } target)
        {
            var wallElapsed = (now - previous).TotalSeconds;
            var elapsed = monotonicSeconds is double currentClock && lastMonotonicSeconds is double previousClock
                ? currentClock - previousClock : wallElapsed;
            // A late WM_TIMECHANGE must not turn clock correction into work.
            // Small clock slews retain monotonic duration; jumps discard this
            // observation, just like a sleep or delayed dispatcher callback.
            if (wallElapsed > 0 && elapsed > 0 && elapsed <= 5 && Math.Abs(wallElapsed - elapsed) <= 1 && RuntimeCompat.IsFinite(input.IdleSeconds))
            {
                var fraction = 1d;
                if (input.IdleSeconds >= Data.Settings.AwayThreshold && RuntimeCompat.IsFinite(input.IdleSeconds))
                    fraction = RuntimeCompat.Clamp((elapsed - (input.IdleSeconds - Data.Settings.AwayThreshold)) / elapsed, 0, 1);
                var end = previous.AddTicks((long)Math.Round((now - previous).Ticks * fraction));
                if (end > previous) AddInterval(previous, end, target, elapsed * fraction);
            }
        }
        LastObservation = now;
        lastMonotonicSeconds = monotonicSeconds;
        var rules=Data.Settings.ActiveProfileId is not null && ColorProfilesService.Active(Data.Settings) is null ? [] : Data.Settings.Rules;
        var websites = Data.Settings.ActiveProfileId is not null && ColorProfilesService.Active(Data.Settings) is null ? [] : Data.Settings.Websites;
        Current = ActivityDecision.Evaluate(input, rules, websites, RecognitionRules.Browsers, Data.Settings.AwayThreshold, Data.Settings.Paused);
    }

    public void ResetObservation() { LastObservation = null; lastMonotonicSeconds = null; Current = new(Data.Settings.Paused?"paused":"away"); }

    private void AddInterval(DateTimeOffset start, DateTimeOffset end, TrackingTarget target, double effectiveSeconds)
    {
        var secondsPerWallSecond = effectiveSeconds / (end - start).TotalSeconds;
        while (start < end)
        {
            var boundary = LogicalDay.NextBoundary(start, Zone, Data.Settings.DayResetMinutes);
            var segmentEnd = boundary < end ? boundary : end;
            var day = LogicalDay.Key(start, Zone, Data.Settings.DayResetMinutes);
            var last = Data.Sessions.LastOrDefault();
            if (last is not null && last.DayKey == day && last.ApplicationIdentifier == target.Identifier && last.EndedAt == start)
            {
                last.EndedAt = segmentEnd;
                last.EffectiveSeconds += (segmentEnd - start).TotalSeconds * secondsPerWallSecond;
            }
            else Data.Sessions.Add(new() { ApplicationIdentifier = target.Identifier, ApplicationName = target.DisplayName,
                CategoryRawValue = target.CategoryRawValue, DayKey = day, StartedAt = start, EndedAt = segmentEnd,
                EffectiveSeconds = (segmentEnd - start).TotalSeconds * secondsPerWallSecond });
            start = segmentEnd;
        }
    }
}
