using System.IO;
using System.Linq;
using System.Text.Json;
using WorkIsland.App;
using WorkIsland.Core;

internal static partial class Program
{
    private static int WriteStartupCommandVectors(string output)
    {
        var rows=new[]{
            (exe:@"D:\Old app\WorkIsland.exe",data:@"D:\中文 数据\PreviewData"),
            (exe:@"D:\New app\WorkIsland.exe",data:@"D:\New app\PreviewData\"),
            (exe:@"D:\New app\WorkIsland.exe",data:@"D:\"),
            (exe:@"D:\symbols $() [&]\WorkIsland.exe",data:@"D:\symbols $() [&]\PreviewData"),
            (exe:@"D:\New app\WorkIsland.exe",data:@"D:\nested\..\PreviewData")
        }.Select(row=>new{row.exe,row.data,command=StartupCommand.Create(row.exe,row.data)}).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);File.WriteAllText(output,JsonSerializer.Serialize(new{commands=rows,scope="Existing exact product StartupCommand.Create, no registry access or process launch."},AtomicStore.JsonOptions));return 0;
    }
}
