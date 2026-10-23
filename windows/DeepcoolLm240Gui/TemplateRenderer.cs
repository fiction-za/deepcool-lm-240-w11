using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DeepcoolLm240.Usb;

namespace DeepcoolLm240Gui;

public sealed class TemplateRenderer
{
    static readonly Regex Placeholder = new(@"\{([a-z.]+)(?::([^}]+))?\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    readonly Dictionary<string, Bitmap> _images = new(StringComparer.OrdinalIgnoreCase);

    public byte[] RenderRgb565(LcdTemplate template, MonitorSnapshot stats)
    {
        using var rtb = new RenderTargetBitmap(
            new PixelSize(Lm240Protocol.Width, Lm240Protocol.Height),
            new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext(true))
            Draw(ctx, template, stats);

        var bgra = new byte[Lm240Protocol.Width * Lm240Protocol.Height * 4];
        var handle = GCHandle.Alloc(bgra, GCHandleType.Pinned);
        try
        {
            rtb.CopyPixels(
                new PixelRect(0, 0, Lm240Protocol.Width, Lm240Protocol.Height),
                handle.AddrOfPinnedObject(),
                bgra.Length,
                Lm240Protocol.Width * 4);
        }
        finally
        {
            handle.Free();
        }

        return Lm240Protocol.FromBgra(bgra, Lm240Protocol.Width * 4);
    }

    void Draw(DrawingContext ctx, LcdTemplate template, MonitorSnapshot stats)
    {
        ctx.FillRectangle(Brush(template.Background), new Rect(0, 0, Lm240Protocol.Width, Lm240Protocol.Height));
        foreach (var el in template.Elements)
        {
            switch (el.Type.ToLowerInvariant())
            {
                case "image":
                    DrawImage(ctx, el, stats);
                    break;
                case "rect":
                    DrawRect(ctx, el);
                    break;
                case "bar":
                    DrawBar(ctx, el, stats);
                    break;
                default:
                    DrawText(ctx, el, stats);
                    break;
            }
        }
    }

    void DrawImage(DrawingContext ctx, LcdElement el, MonitorSnapshot stats)
    {
        var path = stats.GameArtPath;
        var dest = new Rect(el.X, el.Y, el.W <= 0 ? Lm240Protocol.Width : el.W, el.H <= 0 ? Lm240Protocol.Height : el.H);
        using var opacity = ctx.PushOpacity(Math.Clamp(el.Opacity, 0, 1));
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            var bmp = LoadImage(path);
            ctx.DrawImage(bmp, dest);
            return;
        }

        ctx.FillRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.Parse("#1E293B"), 0),
                new GradientStop(Color.Parse("#0F172A"), 0.55),
                new GradientStop(Color.Parse("#EA580C"), 1),
            ],
        }, dest);
    }

    void DrawRect(DrawingContext ctx, LcdElement el)
    {
        IBrush? fill = string.IsNullOrWhiteSpace(el.Fill) ? null : Brush(el.Fill);
        IPen? pen = string.IsNullOrWhiteSpace(el.Stroke)
            ? null
            : new Pen(Brush(el.Stroke), el.StrokeWidth <= 0 ? 1 : el.StrokeWidth);
        using var opacity = ctx.PushOpacity(Math.Clamp(el.Opacity, 0, 1));
        ctx.DrawRectangle(fill, pen, new RoundedRect(new Rect(el.X, el.Y, el.W, el.H), el.Radius));
    }

    void DrawBar(DrawingContext ctx, LcdElement el, MonitorSnapshot stats)
    {
        var track = Brush(el.Track ?? "#1E1E26");
        var fillColor = ResolveColor(el, stats);
        var value = ToDouble(stats.Read(el.Bind ?? "cpu.usage"));
        double max = el.Max <= 0 ? 100 : el.Max;
        double t = Math.Clamp(value / max, 0, 1);
        var bounds = new Rect(el.X, el.Y, el.W, el.H);
        double radius = el.Radius > 0 ? el.Radius : el.H / 2;
        ctx.DrawRectangle(track, null, new RoundedRect(bounds, radius));
        if (t <= 0)
            return;
        var fillRect = new Rect(el.X, el.Y, Math.Max(el.H, el.W * t), el.H);
        ctx.DrawRectangle(new SolidColorBrush(fillColor), null, new RoundedRect(fillRect, radius));
    }

    void DrawText(DrawingContext ctx, LcdElement el, MonitorSnapshot stats)
    {
        string raw = el.Text ?? (el.Bind is null ? "" : "{" + el.Bind + (el.Format is null ? "" : ":" + el.Format) + "}");
        string text = Interpolate(raw, stats);
        var color = ResolveColor(el, stats);
        var weight = string.Equals(el.Weight, "bold", StringComparison.OrdinalIgnoreCase)
            ? FontWeight.Bold
            : FontWeight.Normal;
        var typeface = new Typeface("Inter", FontStyle.Normal, weight);
        var ft = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            el.Size <= 0 ? 16 : el.Size,
            new SolidColorBrush(color));
        double x = el.Align switch
        {
            "right" => el.X - ft.Width,
            "center" => el.X - ft.Width / 2,
            _ => el.X,
        };
        using var opacity = ctx.PushOpacity(Math.Clamp(el.Opacity, 0, 1));
        ctx.DrawText(ft, new Point(x, el.Y));
    }

    Bitmap LoadImage(string path)
    {
        if (_images.TryGetValue(path, out var cached))
            return cached;
        var bmp = new Bitmap(path);
        _images[path] = bmp;
        return bmp;
    }

    static Color ResolveColor(LcdElement el, MonitorSnapshot stats)
    {
        if (!string.IsNullOrWhiteSpace(el.ColorBy) && stats.Read(el.ColorBy) is not null)
            return TempColor(ToDouble(stats.Read(el.ColorBy)));
        return ParseColor(el.Color ?? el.Fill ?? "#E2E8F0");
    }

    static Color TempColor(double temp)
    {
        if (temp < 40) return Color.Parse("#64C8FF");
        if (temp < 60) return Color.Parse("#64FF64");
        if (temp < 75) return Color.Parse("#FFDC32");
        if (temp < 85) return Color.Parse("#FF8C00");
        return Color.Parse("#FF3232");
    }

    static string Interpolate(string pattern, MonitorSnapshot stats) =>
        Placeholder.Replace(pattern, match =>
        {
            var value = stats.Read(match.Groups[1].Value);
            if (value is null)
                return "";
            var format = match.Groups[2].Success ? match.Groups[2].Value : null;
            return value is IFormattable formattable && format is not null
                ? formattable.ToString(format, CultureInfo.InvariantCulture)
                : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        });

    static double ToDouble(object? value) => value switch
    {
        null => 0,
        double d => d,
        IConvertible c => c.ToDouble(CultureInfo.InvariantCulture),
        _ => 0,
    };

    static IBrush Brush(string hex) => new SolidColorBrush(ParseColor(hex));

    static Color ParseColor(string hex) => Color.Parse(hex.StartsWith('#') ? hex : "#" + hex);
}
