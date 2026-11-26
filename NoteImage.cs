using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace NotepadApp;

internal static class NoteImage
{
    private const int MaxStoredDimension = 1200;
    private const int TwipsPerInch = 1440;

    public static bool ContainsPicture(string? richText) =>
        richText?.Contains(@"{\pict", StringComparison.Ordinal) == true;

    public static string CreateRtf(Stream input, Size available, int dpi)
    {
        using var image = Image.FromStream(input, useEmbeddedColorManagement: true, validateImageData: true);
        Size stored = Fit(image.Size, new Size(MaxStoredDimension, MaxStoredDimension));
        Size displayed = Fit(image.Size, available);

        using var bitmap = new Bitmap(stored.Width, stored.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(image, new Rectangle(Point.Empty, stored));
        }

        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        string data = Convert.ToHexString(png.GetBuffer().AsSpan(0, (int)png.Length));
        int displayWidth = displayed.Width * TwipsPerInch / dpi;
        int displayHeight = displayed.Height * TwipsPerInch / dpi;
        return FormattableString.Invariant(
            $@"{{\rtf1\ansi{{\pict\pngblip\picw{stored.Width}\pich{stored.Height}\picwgoal{displayWidth}\pichgoal{displayHeight} {data}}}}}");
    }

    private static Size Fit(Size image, Size available)
    {
        double scale = Math.Min(1, Math.Min((double)available.Width / image.Width, (double)available.Height / image.Height));
        return new Size(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)));
    }
}
