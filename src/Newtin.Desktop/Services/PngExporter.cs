using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtin.Server.Models;
using Newtin.Server.Persistence;

namespace Newtin.Desktop.Services;

/// <summary>Dibuja la pizarra con WPF y la guarda como PNG. Debe llamarse desde el hilo de la interfaz.</summary>
public static class PngExporter
{
    private static readonly Typeface Face = new("Segoe UI");

    public static byte[] Render(WhiteboardSnapshot snapshot)
    {
        var layout = BoardLayoutCalculator.Compute(snapshot);
        var categories = snapshot.Categories.ToDictionary(c => c.Id);
        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brush("#FAF8FF"), null, new Rect(0, 0, layout.Width, layout.Height));
            dc.PushTransform(new TranslateTransform(-layout.X, -layout.Y));

            foreach (var element in snapshot.Elements.OrderBy(e => e.Order))
            {
                switch (element.Type)
                {
                    case ElementType.Stroke: DrawStroke(dc, element); break;
                    case ElementType.Text: DrawText(dc, element); break;
                    case ElementType.Note: DrawNote(dc, element, categories); break;
                }
            }

            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(layout.Width), (int)Math.Ceiling(layout.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static Color? TryColor(string? hex) =>
        hex is not null && Regex.IsMatch(hex, "^#[0-9A-Fa-f]{6}$") ? (Color)ColorConverter.ConvertFromString(hex) : null;

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Brush(string? hex, string fallback = "#17131F") =>
        Brush(TryColor(hex) ?? TryColor(fallback)!.Value);

    private static Color TextColorFor(Color color) =>
        0.299 * color.R + 0.587 * color.G + 0.114 * color.B > 150 ? Color.FromRgb(0x17, 0x13, 0x1F) : Colors.White;

    private static void DrawStroke(DrawingContext dc, WhiteboardElement element)
    {
        var points = element.Points;
        if (points is null || points.Count == 0) return;

        var brush = Brush(element.Color);
        var width = element.Width ?? 3;
        var offset = new Vector(element.X, element.Y);

        if (points.Count == 1)
        {
            dc.DrawEllipse(brush, null, new Point(points[0].X, points[0].Y) + offset, width / 2, width / 2);
            return;
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(points[0].X, points[0].Y) + offset, false, false);
            context.PolyLineTo(points.Skip(1).Select(p => new Point(p.X, p.Y) + offset).ToList(), true, true);
        }
        geometry.Freeze();

        var pen = new Pen(brush, width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private static void DrawText(DrawingContext dc, WhiteboardElement element)
    {
        var size = element.Width ?? 24;
        var text = new FormattedText(element.Text ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            Face, size, Brush(element.Color), 1.0) { LineHeight = size * 1.25 };
        dc.DrawText(text, new Point(element.X, element.Y));
    }

    private static void DrawNote(DrawingContext dc, WhiteboardElement element,
        Dictionary<string, WhiteboardCategory> categories)
    {
        var (x, y) = BoardLayoutCalculator.NotePosition(element);
        var rect = new Rect(x, y, BoardLayoutCalculator.NoteWidth, BoardLayoutCalculator.NoteHeight);
        var fill = TryColor(element.Color) ?? TryColor("#FFF1A8")!.Value;

        WhiteboardCategory? category = null;
        if (element.CategoryId is not null) categories.TryGetValue(element.CategoryId, out category);

        dc.DrawRoundedRectangle(Brush(fill), null, rect, 10, 10);
        dc.PushClip(new RectangleGeometry(rect, 10, 10));

        var headerColor = category is null
            ? Color.FromArgb(0x1F, 0, 0, 0)
            : TryColor(category.Color) ?? Color.FromRgb(0xA0, 0x00, 0x82);
        dc.DrawRectangle(Brush(headerColor), null, new Rect(x, y, rect.Width, 24));

        var label = new FormattedText(category?.Name ?? "Sin categoría", CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, Face, 12,
            Brush(TextColorFor(category is null ? fill : headerColor)), 1.0)
        {
            MaxTextWidth = rect.Width - 16,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };
        label.SetFontWeight(FontWeights.SemiBold);
        dc.DrawText(label, new Point(x + 8, y + 5));

        var body = new FormattedText(element.Text ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            Face, 14, Brush(TextColorFor(fill)), 1.0)
        {
            MaxTextWidth = rect.Width - 16,
            MaxTextHeight = rect.Height - 36,
            Trimming = TextTrimming.CharacterEllipsis,
            LineHeight = 18
        };
        dc.DrawText(body, new Point(x + 8, y + 30));

        dc.Pop();
    }
}