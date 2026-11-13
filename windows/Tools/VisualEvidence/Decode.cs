using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static partial class Program
{
    private static int Decode(string[] args)
    {
        var exit = 0;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Dispatcher.InvokeAsync(async () =>
        {
            var player = new MediaPlayer { Volume = 0, IsMuted = true };
            Window? surface = null;
            try
            {
                var output = Path.GetFullPath(args[2]); Directory.CreateDirectory(output);
                var opened = new TaskCompletionSource(); var ended = new TaskCompletionSource();
                var playback=new System.Diagnostics.Stopwatch();double? playbackEndedSeconds=null,positionAtEndSeconds=null;
                player.MediaOpened += (_, _) => opened.TrySetResult();
                player.MediaEnded += (_, _) => {playbackEndedSeconds=playback.Elapsed.TotalSeconds;positionAtEndSeconds=player.Position.TotalSeconds;ended.TrySetResult();};
                player.MediaFailed += (_, e) => { opened.TrySetException(e.ErrorException); ended.TrySetException(e.ErrorException); };
                player.Open(new Uri(Path.GetFullPath(args[1])));
                await opened.Task.WaitAsync(TimeSpan.FromSeconds(8));
                var width = player.NaturalVideoWidth; var height = player.NaturalVideoHeight;
                // This runtime truncates NaturalDuration to whole seconds.
                // Read local file metadata independently, then also require
                // actual sequential playback through MediaEnded.
                var naturalDuration=player.NaturalDuration.TimeSpan.TotalSeconds;
                var duration = LocalVideoDuration(Path.GetFullPath(args[1]));
                double? expectedCaptureDuration=null;
                if(args.Length>3)
                {
                    using var capture=JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(args[3])));
                    expectedCaptureDuration=capture.RootElement.GetProperty("durationSeconds").GetDouble();
                    if(expectedCaptureDuration<=0||capture.RootElement.GetProperty("frames").GetArrayLength()==0)throw new InvalidDataException("Empty source capture report.");
                }
                var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen()) drawing.DrawVideo(player, new Rect(0, 0, width, height));
                surface = new Window { Content = new DrawingView(visual), Width = width, Height = height,
                    ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -2000, Top = -2000 };
                surface.Show();playback.Start();player.Play();
                var sheet = new DrawingVisual(); var samples = new List<object>();
                var sampleCount=Math.Max(1,(int)Math.Ceiling(duration-.3));
                using (var drawing = sheet.RenderOpen())
                {
                    for (var index = 0; index < sampleCount; index++)
                    {
                        await Task.Delay(index == 0 ? 300 : 1000);
                        var frame = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); frame.Render(visual);
                        Save(frame, Path.Combine(output, $"decoded-{index:00}.png"));
                        drawing.DrawImage(frame, new Rect(index % 4 * width, index / 4 * height, width, height));
                        samples.Add(new { index, positionSeconds = player.Position.TotalSeconds });
                    }
                }
                await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var contact = new RenderTargetBitmap(width * 4, height * ((sampleCount+3)/4), 96, 96, PixelFormats.Pbgra32); contact.Render(sheet);
                Save(contact, Path.Combine(output, "decoded-contact-sheet.png"));
                var verified=(expectedCaptureDuration is {} expected?Math.Abs(duration-expected)<.15:duration>=15)&&width>0&&height>0&&ended.Task.IsCompletedSuccessfully&&playbackEndedSeconds is {} elapsed&&Math.Abs(elapsed-duration)<.35;
                if(!verified)exit=1;
                File.WriteAllText(Path.Combine(output, "video-playback.json"), JsonSerializer.Serialize(new
                { passed=verified, file = Path.GetFullPath(args[1]), width, height, durationSeconds = duration,naturalDurationSeconds=naturalDuration,durationSource="Local Windows System.Media.Duration (100ns units)",expectedCaptureDurationSeconds=expectedCaptureDuration,playbackEndedSeconds,positionAtEndSeconds,
                    decodedSamples = samples, mediaEnded = ended.Task.IsCompletedSuccessfully,
                    method = "Sequential Windows MediaPlayer MP4 decode at original resolution through end of media, without seeking or time scaling" }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception error) { Console.Error.WriteLine(error); exit = 1; }
            finally { player.Close(); surface?.Close(); app.Shutdown(); }
        });
        app.Run(); return exit;
    }
    private static double LocalVideoDuration(string file)
    {
        object? shell=null,folder=null,item=null;
        try
        {
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            folder=((dynamic)shell!).NameSpace(Path.GetDirectoryName(file));item=((dynamic)folder!).ParseName(Path.GetFileName(file));
            object value=((dynamic)item!).ExtendedProperty("System.Media.Duration");var ticks=Convert.ToInt64(value);if(ticks<=0)throw new InvalidDataException("No precise local video duration: "+file);return ticks/(double)TimeSpan.TicksPerSecond;
        }
        finally
        {
            foreach(var value in new[]{item,folder,shell})if(value is not null&&System.Runtime.InteropServices.Marshal.IsComObject(value))System.Runtime.InteropServices.Marshal.FinalReleaseComObject(value);
        }
    }
}
