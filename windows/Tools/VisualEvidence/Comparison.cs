using System;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
internal static partial class Program
{
 static int Compare(string[] args)
 {
  if(args.Length!=5)throw new ArgumentException("compare <Mac-directory> <Windows-directory> <output.png> <product.dll>");
  var version=ProductVersion(args[4]);
  var rows=new (string Title,string Mac,string Windows)[]
  {
   ("Complete tracking / completed matrix","01-collapsed-initial.png","01-complete-tracking-2x.png"),
   ("Navigation / vector controls","24-motion-01-nav-cua.png","02-navigation-2x.png"),
   ("Settings top / Windows login launch supported, unchecked in fixture","18-settings-top-native-cua.png","03-settings-top-2x.png"),
   ("Background and opacity","19-settings-background-opacity-cua.png","04-settings-background-2x.png"),
   ("Time format and goal","20-settings-time-goal-native-cua.png","05-settings-time-goal-2x.png"),
   ("History list / isolated totals differ","12-history-list-cua.png","08-history-list-2x.png"),
   ("Statistics / isolated historic days differ","13-history-statistics-cua.png","09-history-statistics-2x.png"),
   ("History details / isolated sample app names and values","14-history-current-detail-cua.png","10-history-detail-2x.png"),
   ("Complete away / original video frame, pointer occlusion","26-video-proof-2.0s.png","11-complete-away-2x.png")
  };
  const int width=1180,rowHeight=184;var height=52+rows.Length*rowHeight+46;
  var visual=new DrawingVisual();
  void Label(DrawingContext dc,string text,double x,double y,double size,Brush brush)
   =>dc.DrawText(new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,brush,1),new Point(x,y));
  using(var dc=visual.RenderOpen())
  {
   dc.DrawRectangle(Brushes.White,null,new Rect(0,0,width,height));
   Label(dc,"Mac 0.8.23 (73) - actual M6 captures",16,8,17,Brushes.Black);
   Label(dc,$"Windows {version.ToString(3)} ({version.Revision}) - actual WPF window / isolated test data",598,8,17,Brushes.Black);
   for(int i=0;i<rows.Length;i++)
   {
    var y=52+i*rowHeight;Label(dc,rows[i].Title,16,y-21,13,Brushes.DimGray);
    var mac=BitmapFrame.Create(new Uri(Path.Combine(args[1],rows[i].Mac)),BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
    var windows=BitmapFrame.Create(new Uri(Path.Combine(args[2],rows[i].Windows)),BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
    if(mac.PixelWidth!=566 || mac.PixelHeight!=152 || windows.PixelWidth!=566 || windows.PixelHeight!=152)throw new Exception("Expected original 566x152 canvases: "+rows[i].Title);
    dc.DrawImage(mac,new Rect(16,y,566,152));dc.DrawImage(windows,new Rect(598,y,566,152));
   }
   Label(dc,"Same 566 x 152 canvas, 283 x 76 logical footprint; Windows actual system DPI 125%, exported at 192 DPI.",16,height-40,12,Brushes.DimGray);
   Label(dc,"References remain unchanged. Pointer/backdrop/font differences are visible. Missing Mac variants are source-derived, outside this sheet.",16,height-23,12,Brushes.DimGray);
  }
  var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
  var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[3]))!);using(var file=File.Create(args[3]))encoder.Save(file);
  SheetEvidence(args[3],args[4],rows.SelectMany(row=>new[]{Path.Combine(args[1],row.Mac),Path.Combine(args[2],row.Windows)}));
  Console.WriteLine(args[3]);return 0;
 }
}

