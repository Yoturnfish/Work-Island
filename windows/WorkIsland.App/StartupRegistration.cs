using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace WorkIsland.App;

public interface IStartupRegistration
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}

public static class StartupCommand
{
    public static string Create(string executable,string dataDirectory) => Quote(Path.GetFullPath(executable))+
        " --start-minimized --data-dir "+Quote(Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory)));
    private static string Quote(string value)
    {
        var result=new StringBuilder("\"");var slashes=0;
        foreach(var ch in value)
        {
            if(ch=='\\'){slashes++;continue;}
            result.Append('\\',ch=='"'?slashes*2+1:slashes);result.Append(ch);slashes=0;
        }
        return result.Append('\\',slashes*2).Append('"').ToString();
    }
}

public sealed class WindowsStartupRegistration(string executable,string dataDirectory,string keyPath=WindowsStartupRegistration.RunKey) : IStartupRegistration
{
    public const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName="WorkIsland";
    public string Command { get; } = StartupCommand.Create(executable,dataDirectory);
    private object? Existing(){using var key=Registry.CurrentUser.OpenSubKey(keyPath);return key?.GetValue(ValueName);}
    private static bool Owned(object? value)
    {
        if(value is not string command)return false;
        var first=command.StartsWith('"') ? command[1..].Split('"')[0] : command.Split(' ')[0];
        return Path.GetFileName(first).Equals("WorkIsland.exe",StringComparison.OrdinalIgnoreCase);
    }
    public bool IsEnabled => Existing() is string command && string.Equals(command,Command,StringComparison.OrdinalIgnoreCase);
    public void SetEnabled(bool enabled)
    {
        var existing=Existing();
        if(existing is not null && !Owned(existing))throw new InvalidOperationException("已有其他程序的同名登录项，未覆盖。");
        if(!enabled)
        {
            if(existing is null)return;
            using var key=Registry.CurrentUser.OpenSubKey(keyPath,true);key?.DeleteValue(ValueName,false);return;
        }
        if(!File.Exists(executable))throw new FileNotFoundException("当前程序文件不存在，未注册登录项。",executable);
        using var target=Registry.CurrentUser.CreateSubKey(keyPath,true);
        target.SetValue(ValueName,Command,RegistryValueKind.String);
    }
}

// All acceptance windows inject this provider or another isolated test double.
// This file is a fixture, never a Windows login registration.
public sealed class IsolatedStartupRegistration(string path,string command) : IStartupRegistration
{
    public bool IsEnabled { get { if(!File.Exists(path))return false;using var document=JsonDocument.Parse(File.ReadAllText(path));return document.RootElement.GetProperty("enabled").GetBoolean(); } }
    public void SetEnabled(bool enabled)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,JsonSerializer.Serialize(new{enabled,command,isolated=true}));
    }
}
