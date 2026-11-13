using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace WorkIsland.App;

public static class InstallationIdentity
{
    public const string MarkerName="installation.ini";
    public static string MutexName(Guid id)=>"Local\\WorkIsland-Installed-"+id.ToString("D").ToUpperInvariant();
    public static Mutex? Acquire(string directory)
    {
        var marker=Path.Combine(directory,MarkerName);
        if(!File.Exists(marker))return null;
        if(new FileInfo(marker).Length>8192)throw new InvalidDataException("安装标识文件过大，请重新运行安装程序。");
        var values=File.ReadAllLines(marker).Where(line=>line.StartsWith("AppId=",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(values.Length!=1 || !Guid.TryParseExact(values[0][6..].Trim(),"D",out var id))
            throw new InvalidDataException("安装标识无效，请重新运行安装程序。");
        return new Mutex(false,MutexName(id));
    }
}
