using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class Program
{
    [STAThread] private static int Main(string[] args)
    {
        if (args.Length != 2) { Console.Error.WriteLine("MotionEvidence <capture-directory> <output.mp4>"); return 2; }
        try
        {
            var root = Path.GetFullPath(args[0]);
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "motion-capture.json")));
            var frames = report.RootElement.GetProperty("frames").EnumerateArray().ToArray();
            var output = Path.GetFullPath(args[1]);
            BitmapSource Load(string name)
            {
                using var stream = File.OpenRead(Path.Combine(root, "frames", name));
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                return new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgr32, null, 0);
            }
            var first = Load(frames[0].GetProperty("file").GetString()!);
            // H.264 requires even dimensions. Pad at right/bottom without rescaling the captured pixels.
            var width = (first.PixelWidth + 1) & ~1; var height = (first.PixelHeight + 1) & ~1;
            using var writer = new Mp4Writer(output, width, height, 50);
            var timings = new List<object>();
            for (var index = 0; index < frames.Length; index++)
            {
                var frame = Load(frames[index].GetProperty("file").GetString()!);
                if (frame.PixelWidth != first.PixelWidth || frame.PixelHeight != first.PixelHeight)
                    throw new InvalidDataException("Capture dimensions changed during recording.");
                var pixels = new byte[width * height * 4];
                frame.CopyPixels(pixels, width * 4, 0);
                var time = frames[index].GetProperty("elapsedSeconds").GetDouble();
                var end = index + 1 < frames.Length ? frames[index + 1].GetProperty("elapsedSeconds").GetDouble()
                    : report.RootElement.GetProperty("durationSeconds").GetDouble();
                var startTicks = (long)Math.Round(time * 10_000_000);
                var durationTicks = Math.Max(1, (long)Math.Round(end * 10_000_000) - startTicks);
                writer.Write(pixels, startTicks, durationTicks);
                timings.Add(new { frame = index, startTicks, durationTicks });
            }
            writer.Finish();
            File.WriteAllText(output + ".json", JsonSerializer.Serialize(new
            {
                passed = File.Exists(output) && new FileInfo(output).Length > 0, output, width, height,
                captureWidth = first.PixelWidth, captureHeight = first.PixelHeight, frameCount = frames.Length,
                method = "Windows Media Foundation H.264 MP4; measured source timestamps retained; no resizing, audio or slow motion",
                source = Path.Combine(root, "motion-capture.json"), timings
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(output); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
