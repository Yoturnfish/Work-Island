using System;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static partial class Program
{
    static int Screens(string[] args)
    {
        if(args.Length!=4)throw new ArgumentException("screens <Windows-directory> <output.png> <product.dll>");
        var version=ProductVersion(args[3]);
        var rows = new[] { ("Tracking / external shadow", "01-tracking-screen.png"),
            ("Navigation / same compact frame", "03-navigation-screen.png"),
            ("Settings / original page structure", "04-settings-screen.png"),
            ("Glass / fixed 25% host backdrop blend", "07-glass-screen.png"),
            ("Glass settings / 30% page shade", "08-glass-settings-screen.png"),
            ("Away / cup and source title opacity", "10-away-screen.png") };
        var visual = new DrawingVisual(); const int width = 880, height = 650;
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            void Label(string text, double x, double y, double size) => drawing.DrawText(new FormattedText(text, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, Brushes.Black, 1), new Point(x, y));
            Label($"Work Island Windows {version.ToString(3)} ({version.Revision}) / actual OS window screenshots", 16, 10, 18);
            Label("125% physical screen DPI / isolated test records and owned stripe background", 16, 35, 12);
            for (int i = 0; i < rows.Length; i++)
            {
                var x = 16 + i % 2 * 438; var y = 82 + i / 2 * 183;
                Label(rows[i].Item1, x, y - 20, 13);
                var bitmap = BitmapFrame.Create(new Uri(Path.Combine(args[1], rows[i].Item2)), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                drawing.DrawImage(bitmap, new Rect(x, y, bitmap.PixelWidth, bitmap.PixelHeight));
            }
            Label("Original screenshot pixels retained. Mac reference comparison is supplied separately.", 16, 626, 12);
        }
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(visual);
        Save(image, args[2]);
        SheetEvidence(args[2],args[3],rows.Select(row=>Path.Combine(args[1],row.Item2)));
        return 0;
    }
}
