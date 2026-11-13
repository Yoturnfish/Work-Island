using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace WorkIsland.Core;

public sealed class AtomicStore(string path)
{
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, PropertyNameCaseInsensitive = true };
    public string Path { get; } = System.IO.Path.GetFullPath(path);
    public string? RecoveryMessage { get; private set; }
    private bool preserveInvalidPrimary;
    private byte[]? legacyOriginal;
    private string? preservedInvalidPrimaryCopy;

    public AppState Load()
    {
        if (!File.Exists(Path))
        {
            if(!File.Exists(Path+".bak"))
            {
                legacyOriginal=null;
                return new(){Settings=new(){GoalSeconds=2*3600,HasCompletedOnboarding=false,
                    Language=System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="zh"?"zh":"en"}};
            }
            try
            {
                var backup=Read(Path+".bak",out var original);
                legacyOriginal=original;
                RecoveryMessage="主数据文件缺失，已从上一份备份恢复。";
                return backup;
            }
            catch(Exception error)when(error is JsonException or InvalidDataException or IOException)
            {
                throw new InvalidDataException("主数据文件缺失，备份无法读取。原备份已保留，应用不会创建空记录覆盖它。",error);
            }
        }
        try { var result=Read(Path,out var original);legacyOriginal=original;return result; }
        catch (Exception error) when (error is JsonException or InvalidDataException or IOException)
        {
            if (!File.Exists(Path + ".bak")) throw new InvalidDataException("数据文件无法读取，已保留原文件。请备份后检查；应用不会覆盖它。", error);
            var backup = Read(Path + ".bak",out var original);
            legacyOriginal=original;
            preserveInvalidPrimary = true;
            RecoveryMessage = "已从上一份备份恢复。损坏的原文件将在保存前留存。";
            return backup;
        }
    }
    private static AppState Read(string file,out byte[]? original)
    {
        original=null;
        var bytes=File.ReadAllBytes(file);
        var result = JsonSerializer.Deserialize<AppState>(bytes, JsonOptions) ?? throw new InvalidDataException("数据为空。");
        result.Validate();
        var migrated=result.SchemaVersion==1;
        ColorProfilesService.UpgradeLegacy(result);
        if(result.Settings.ActiveProfileId is not null && ColorProfilesService.Active(result.Settings) is null)
            ColorProfilesService.Initialize(result.Settings);
        result.Validate();
        // Archive the same bytes that were validated and migrated, even if
        // the source path is moved or replaced before the first save.
        if(migrated)original=bytes;
        return result;
    }
    public void Save(AppState state)
    {
        state.Validate();
        if(state.SchemaVersion==2)ColorProfilesService.SnapshotActive(state.Settings);
        Commit(JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions));
    }
    public string SaveIfChanged(AppState state,string? previousSnapshot,bool force=false)
    {
        if(state.SchemaVersion==2)ColorProfilesService.SnapshotActive(state.Settings);
        var snapshot=JsonSerializer.Serialize(state,JsonOptions);
        if(force||snapshot!=previousSnapshot)
        {
            state.Validate();
            Commit(Encoding.UTF8.GetBytes(snapshot));
        }
        // A failed commit must never advance the caller's saved snapshot.
        return snapshot;
    }
    private void Commit(byte[] bytes)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        if(legacyOriginal is { } original)
        {
            using(var stream=new FileStream(Path+".pre-v2-"+Guid.NewGuid().ToString("N"),FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough))
            {stream.Write(original);stream.Flush(true);}
            legacyOriginal=null;
        }
        var recovering = preserveInvalidPrimary;
        // A damaged source may have been moved away after Load or a rejected
        // save. There is then nothing at this path to copy, but retain recovery
        // mode through the commit so the known-good backup is not replaced.
        if (recovering && File.Exists(Path) && !HasPreservedInvalidPrimary())
        {
            var copy=Path+".corrupt-"+Guid.NewGuid().ToString("N");
            File.Copy(Path,copy);
            preservedInvalidPrimaryCopy=copy;
        }
        var temp = Path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { stream.Write(bytes); stream.Flush(true); }
        if (File.Exists(Path)) File.Replace(temp, Path, recovering ? null : Path + ".bak");
        else File.Move(temp, Path);
        preserveInvalidPrimary = false;
        preservedInvalidPrimaryCopy=null;
    }
    private bool HasPreservedInvalidPrimary()
    {
        if(preservedInvalidPrimaryCopy is null)return false;
        try
        {
            // Save retries can follow a sharing violation for many timer ticks.
            // Reuse only readable, matching evidence; preserve a new source
            // version or replace missing/changed evidence without deleting any.
            using var primary=File.OpenRead(Path);
            using var copy=File.OpenRead(preservedInvalidPrimaryCopy);
            return primary.Length==copy.Length&&RuntimeCompat.HashData(primary).AsSpan().SequenceEqual(RuntimeCompat.HashData(copy));
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException){return false;}
    }
}
