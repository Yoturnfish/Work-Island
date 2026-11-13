namespace WorkIsland.Core;

public static class ColorProfilesService
{
    public static readonly string[] Accents=["green","blue","yellow","cyan","pink","orange","purple"];
    public const string DefaultId="8e3eb079-9dda-4d92-8fd3-4b3c84aa3a38";
    public static ColorProfile? Active(Settings settings) => settings.ColorProfiles.FirstOrDefault(p=>string.Equals(p.Id,settings.ActiveProfileId,StringComparison.OrdinalIgnoreCase));

    public static void UpgradeLegacy(AppState state)
    {
        if(state.SchemaVersion!=1)return;
        // The top-level fields are the last edited active profile in v1;
        // profile snapshots may predate edits made since the last switch.
        Initialize(state.Settings);
        state.SchemaVersion=2;
    }

    public static void Initialize(Settings settings)
    {
        if(settings.ActiveProfileId is not null)
        {
            if(Active(settings) is null)ClearCurrent(settings);
            return;
        }
        var current=settings.ColorProfiles.FirstOrDefault(p=>p.Accent==settings.Accent);
        if(current is null)
        {
            current=new(){Accent=settings.Accent,Title=settings.DisplayTitle,ProgressStyle=settings.ProgressStyle,Rules=settings.Rules.ToList(),Websites=settings.Websites.ToList()};
            settings.ColorProfiles.Add(current);
        }
        current.Title=settings.DisplayTitle;current.ProgressStyle=settings.ProgressStyle;current.Rules=settings.Rules.ToList();current.Websites=settings.Websites.ToList();
        var original=settings.ColorProfiles.FirstOrDefault(p=>p.Accent=="green");
        if(original is null){original=new(){Id=DefaultId,Accent="green"};settings.ColorProfiles.Insert(0,original);}
        original.IsDefault=true;settings.ActiveProfileId=current.Id;
    }

    public static void SnapshotActive(Settings settings)
    {
        if(Active(settings) is not { } current)return;
        current.Title=settings.DisplayTitle;current.ProgressStyle=settings.ProgressStyle;current.Rules=settings.Rules.ToList();current.Websites=settings.Websites.ToList();
    }

    public static void Activate(Settings settings,string id)
    {
        SnapshotActive(settings);
        settings.ActiveProfileId=id;
        if(Active(settings) is not { } next){ClearCurrent(settings);return;}
        settings.Accent=next.Accent;settings.DisplayTitle=next.Title;settings.ProgressStyle=next.ProgressStyle;settings.Rules=next.Rules.ToList();settings.Websites=next.Websites.ToList();
    }

    public static ColorProfile SelectAccent(Settings settings,string accent)
    {
        if(!Accents.Contains(accent))throw new ArgumentException("无效颜色。",nameof(accent));
        Initialize(settings);
        var next=settings.ColorProfiles.FirstOrDefault(p=>p.Accent==accent);
        if(next is null)next=Create(settings,accent,"");
        else Activate(settings,next.Id);
        return next;
    }

    public static ColorProfile Create(Settings settings,string accent,string name)
    {
        if(!Accents.Contains(accent))throw new ArgumentException("无效颜色。",nameof(accent));
        Initialize(settings);SnapshotActive(settings);
        var profile=new ColorProfile {Accent=accent,Title=NormalizeTitle(name)}; // A new theme is deliberately empty.
        settings.ColorProfiles.Add(profile);Activate(settings,profile.Id);return profile;
    }

    public static void Rename(Settings settings,string id,string name)
    {
        var profile=settings.ColorProfiles.FirstOrDefault(p=>string.Equals(p.Id,id,StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("组合不存在。",nameof(id));
        profile.Title=NormalizeTitle(name);
        if(string.Equals(settings.ActiveProfileId,id,StringComparison.OrdinalIgnoreCase))settings.DisplayTitle=profile.Title;
    }

    public static bool Delete(Settings settings,string id)
    {
        var profile=settings.ColorProfiles.FirstOrDefault(p=>string.Equals(p.Id,id,StringComparison.OrdinalIgnoreCase));
        if(profile is null || profile.IsDefault)return false;
        settings.ColorProfiles.Remove(profile);
        // Retain the missing ID: neither switching nor restart may silently
        // select the default profile and its broader application rules.
        if(string.Equals(settings.ActiveProfileId,id,StringComparison.OrdinalIgnoreCase))ClearCurrent(settings);
        return true;
    }
    private static void ClearCurrent(Settings settings){settings.Rules=[];settings.Websites=[];settings.DisplayTitle="";settings.ProgressStyle="matrix";}
    public static string NormalizeTitle(string name)
    {
        var value=name.Trim();if(value.Length<=60)return value;
        var length=char.IsHighSurrogate(value[59])?59:60;return value[..length];
    }
}
