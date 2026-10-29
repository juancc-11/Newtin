using Newtin.Server.Models;

namespace Newtin.Server.Persistence;

public sealed record BoardLayout(double X, double Y, double Width, double Height);

public static class BoardLayoutCalculator
{
    public const double BoardWidth = 2400;
    public const double BoardHeight = 1500;
    public const double NoteWidth = 210;
    public const double NoteHeight = 150;
    public const double Margin = 40;

    private const double MinWidth = 400;
    private const double MinHeight = 300;
    private const double MaxSize = 6000;

    /// <summary>Las notas se dibujan siempre dentro de la pizarra, igual que en la página web.</summary>
    public static (double X, double Y) NotePosition(WhiteboardElement note) =>
        (Math.Clamp(note.X, 0, BoardWidth - NoteWidth), Math.Clamp(note.Y, 0, BoardHeight - NoteHeight));

    /// <summary>Tamaño estimado de un texto libre (la medida exacta depende de la tipografía).</summary>
    public static (double Width, double Height) TextSize(WhiteboardElement text)
    {
        var size = text.Width ?? 24;
        var lines = (text.Text ?? "").Split('\n');
        return (Math.Max(lines.Max(line => line.Length), 1) * size * 0.6, lines.Length * size * 1.25);
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) Bounds(WhiteboardElement element)
    {
        switch (element.Type)
        {
            case ElementType.Note:
                var (noteX, noteY) = NotePosition(element);
                return (noteX, noteY, noteX + NoteWidth, noteY + NoteHeight);

            case ElementType.Text:
                var (textWidth, textHeight) = TextSize(element);
                return (element.X, element.Y, element.X + textWidth, element.Y + textHeight);

            default:
                var points = element.Points ?? [];
                if (points.Count == 0) return (element.X, element.Y, element.X, element.Y);

                var pad = (element.Width ?? 3) / 2;
                return (
                    points.Min(p => p.X) + element.X - pad, points.Min(p => p.Y) + element.Y - pad,
                    points.Max(p => p.X) + element.X + pad, points.Max(p => p.Y) + element.Y + pad);
        }
    }

    /// <summary>Zona que ocupa el contenido, con margen. Así las imágenes no incluyen toda la pizarra vacía.</summary>
    public static BoardLayout Compute(WhiteboardSnapshot snapshot)
    {
        if (snapshot.Elements.Count == 0) return new BoardLayout(0, 0, 800, 500);

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var element in snapshot.Elements)
        {
            var box = Bounds(element);
            minX = Math.Min(minX, box.MinX);
            minY = Math.Min(minY, box.MinY);
            maxX = Math.Max(maxX, box.MaxX);
            maxY = Math.Max(maxY, box.MaxY);
        }

        var x = Math.Floor(minX - Margin);
        var y = Math.Floor(minY - Margin);
        return new BoardLayout(
            x, y,
            Math.Clamp(Math.Ceiling(maxX - x + Margin), MinWidth, MaxSize),
            Math.Clamp(Math.Ceiling(maxY - y + Margin), MinHeight, MaxSize));
    }

    public static double ContentBottom(WhiteboardSnapshot snapshot) =>
        snapshot.Elements.Count == 0 ? 0 : snapshot.Elements.Max(e => Bounds(e).MaxY);
}