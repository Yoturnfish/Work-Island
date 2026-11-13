using WorkIsland.App;

if(args.Length<1)return 2;
var file=Path.GetFullPath(args[0]);Directory.CreateDirectory(Path.GetDirectoryName(file)!);
File.WriteAllBytes(file,TrayArtwork.Ico("tracking","green"));
if(args.Length>1)
{
    Directory.CreateDirectory(args[1]);
    foreach(var state in new[]{"tracking","away","paused"})
    {
        using var bitmap=TrayArtwork.Render(state,"green",64);bitmap.Save(Path.Combine(args[1],state+".png"));
    }
}
Console.WriteLine(file);return 0;
