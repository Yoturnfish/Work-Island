using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static partial class Program
{
    static int IconAlignment(string[] args)
    {
        if (args.Length != 6) throw new ArgumentException("icons <Mac-dir> <Windows-dir> <pressed.png> <output.png> <product.dll>");
        BitmapSource Load(string path) => BitmapFrame.Create(new Uri(Path.GetFullPath(path)), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var macNavigation = Path.Combine(args[1], "24-motion-01-nav-cua.png");
        var macCup = Path.Combine(args[1], "25-passive-current-state-native-shadow.png");
        var windowsNavigation = Path.Combine(args[2], "02-navigation-2x.png");
        var windowsCup = Path.Combine(args[2], "11-complete-away-2x.png");
        var drawing = new DrawingVisual();
        void Label(DrawingContext dc, string text, double x, double y, double size = 14) => dc.DrawText(
            new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, Brushes.Black, 1), new Point(x, y));
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1180, 690));
            Label(dc, "Mac 0.8.23 (73): original reference", 16, 12, 17);
            Label(dc, $"Windows {ProductVersion(args[5]).ToString(3)}: aligned icons", 598, 12, 17);
            Label(dc, "Navigation: common icon slot and label baseline", 16, 45);
            dc.DrawImage(Load(macNavigation), new Rect(16, 70, 566, 152));
            dc.DrawImage(Load(windowsNavigation), new Rect(598, 70, 566, 152));
            Label(dc, "Cup: visible proportions and Mac optical centering", 16, 242);
            // Extract the original 566x152 body from the larger native-shadow
            // capture; do not scale, recolor, smooth or redraw reference pixels.
            dc.DrawImage(new CroppedBitmap(Load(macCup), new Int32Rect(47, 39, 566, 152)), new Rect(16, 268, 566, 152));
            dc.DrawImage(Load(windowsCup), new Rect(598, 268, 566, 152));
            Label(dc, "Pressed feedback: Mac source -> actual Windows mouse-down", 16, 442);
            Label(dc, "NavigationItemStyle / DesignSystem.swift", 30, 490, 17);
            Label(dc, "RoundedRectangle: corner radius 8", 30, 528, 17);
            Label(dc, "pressedSurface: white opacity 0.08", 30, 556, 17);
            Label(dc, "pressed foreground opacity: 0.55", 30, 584, 17);
            Label(dc, "No held-press Mac screenshot was supplied.", 30, 616, 13);
            dc.DrawImage(Load(args[3]), new Rect(598, 470, 566, 152));
            Label(dc, "Original pixels retained; Mac cup body crop (47,39,566,152). Windows: WPF 2x exports, including actual routed mouse-down.", 16, 651, 12);
            Label(dc, "Windows vector contours follow the reference; they are not Apple's SF Symbols font. Fonts, backdrop and animation phase differ.", 16, 670, 12);
        }
        var bitmap = new RenderTargetBitmap(1180, 690, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing);
        Save(bitmap, args[4]);
        SheetEvidence(args[4], args[5], new[] { macNavigation, macCup, windowsNavigation, windowsCup, args[3] });
        Console.WriteLine(args[4]); return 0;
    }
}
