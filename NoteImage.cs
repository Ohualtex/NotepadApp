using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text.RegularExpressions;

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

    public static string FitPreview(string richText, int width, int height, out int tallestPicture)
    {
        tallestPicture = 0;
        if (!ContainsPicture(richText)) return richText;

        int maxPictureHeight = 0;
        string preview = Regex.Replace(richText, @"(?<!\\)\\picwgoal([0-9]+)\s*\\pichgoal([0-9]+)", match =>
        {
            if (!int.TryParse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int sourceWidth) ||
                !int.TryParse(match.Groups[2].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int sourceHeight) ||
                sourceWidth <= 0 || sourceHeight <= 0) return match.Value;

            Size fitted = Fit(new Size(sourceWidth, sourceHeight), new Size(width, height));
            maxPictureHeight = Math.Max(maxPictureHeight, fitted.Height);
            return FormattableString.Invariant($@"\picwgoal{fitted.Width}\pichgoal{fitted.Height}");
        });
        tallestPicture = maxPictureHeight;
        return preview;
    }

    private static Size Fit(Size image, Size available)
    {
        double scale = Math.Min(1, Math.Min((double)available.Width / image.Width, (double)available.Height / image.Height));
        return new Size(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)));
    }
}
