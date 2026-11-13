using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.Json;
using System.Collections.Generic;
internal static partial class Program {
 [STAThread] static int Main(string[] args) {
  if(args.Length>0 && args[0]=="icons")return IconAlignment(args);
  if(args.Length>0 && args[0]=="compare")return Compare(args);
  if(args.Length>0 && args[0]=="screens")return Screens(args);
  if(args.Length>0 && args[0]=="decode")return Decode(args);
  int exit=0; var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
  app.Dispatcher.InvokeAsync(async()=>{try {
   Directory.CreateDirectory(args[1]); var proofs=new List<object>();
   foreach(var file in Directory.GetFiles(args[0],"*.mov")) {
    var player=new MediaPlayer {Volume=0,IsMuted=true,ScrubbingEnabled=true};
    var opened=new TaskCompletionSource<bool>(); player.MediaOpened+=(_,_)=>opened.TrySetResult(true);
    player.MediaFailed+=(_,e)=>opened.TrySetException(e.ErrorException);
    player.Open(new Uri(file));
    if(await Task.WhenAny(opened.Task,Task.Delay(8000))!=opened.Task)throw new Exception("Media open timeout: "+file);
    await opened.Task;
    var width=player.NaturalVideoWidth;var height=player.NaturalVideoHeight;var duration=player.NaturalDuration.TimeSpan.TotalSeconds;
    var visual=new DrawingVisual(); using(var dc=visual.RenderOpen())dc.DrawVideo(player,new Rect(0,0,width,height));
    var surface=new System.Windows.Window {Content=new System.Windows.Controls.Border {Child=new DrawingView(visual)},Width=width/2,Height=height/2,ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None,Left=-2000,Top=-2000};
    surface.Show();
    var dir=Path.Combine(args[1],Path.GetFileNameWithoutExtension(file));Directory.CreateDirectory(dir);
    player.Position=TimeSpan.Zero;player.Play();
    var sheet=new DrawingVisual();var positions=new List<double>();
    using(var context=sheet.RenderOpen()) {
     for(int i=0;i<16;i++) {
      await Task.Delay(1000);var actual=player.Position.TotalSeconds;positions.Add(actual);
      var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
      Save(bitmap,Path.Combine(dir,$"frame-{i:00}.png"));context.DrawImage(bitmap,new Rect((i%4)*width,(i/4)*height,width,height));
     }
    }
    var contact=new RenderTargetBitmap(width*4,height*4,96,96,PixelFormats.Pbgra32);contact.Render(sheet);Save(contact,Path.Combine(dir,"contact-sheet.png"));
    proofs.Add(new {file,width,height,duration,samplePositions=positions,method="Sequential original MOV playback using Windows WPF MediaPlayer, 1-second decoded-frame sampling; original pixels retained."});
    player.Close();surface.Close();
   }
   File.WriteAllText(Path.Combine(args[1],"video-decoding.json"),JsonSerializer.Serialize(proofs,new JsonSerializerOptions {WriteIndented=true}));
  } catch(Exception e) {exit=1;Console.WriteLine(e);} finally {app.Shutdown();}});
  app.Run();return exit;
 }
 static void Save(BitmapSource image,string path) {var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var f=File.Create(path);encoder.Save(f);}
 private sealed class DrawingView:FrameworkElement {readonly DrawingVisual visual;public DrawingView(DrawingVisual v){visual=v;AddVisualChild(v);}protected override int VisualChildrenCount=>1;protected override Visual GetVisualChild(int index)=>visual;}
}

