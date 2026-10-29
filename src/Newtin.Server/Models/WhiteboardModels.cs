namespace Newtin.Server.Models;

public enum ElementType { Stroke, Note, Text }

public sealed record PointD(double X, double Y);

/// <summary>Estado oficial de un elemento. Es inmutable: cada cambio crea una versión nueva.</summary>
public sealed record WhiteboardElement(
    string Id,
    ElementType Type,
    double X,
    double Y,
    string? Text,
    IReadOnlyList<PointD>? Points,
    string Color,
    double? Width,
    string? CategoryId,
    string Author,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt,
    int Version,
    long Order);

public sealed record WhiteboardCategory(string Id, string Name, string Color);

public sealed record WhiteboardSnapshot(
    IReadOnlyList<WhiteboardElement> Elements,
    IReadOnlyList<WhiteboardCategory> Categories);

public sealed record ClientsInfo(int Count, IReadOnlyList<string> Names);

public sealed record JoinResult(WhiteboardSnapshot Snapshot, ClientsInfo Clients);

public sealed record CategoryDeletion(string Id, IReadOnlyList<WhiteboardElement> UpdatedElements);

public sealed record StrokeProgressEvent(string Id, string Color, double Width, IReadOnlyList<PointD> Points, string Author);

// Entradas del navegador: el servidor decide fechas, autor, versión y orden; nunca se confía en ellos.
public sealed record ElementInput(
    string? Id,
    ElementType? Type,
    double X,
    double Y,
    string? Text,
    List<PointD>? Points,
    string? Color,
    double? Width,
    string? CategoryId);

public sealed record CategoryInput(string? Id, string? Name, string? Color);

public sealed record StrokeProgressInput(string? Id, string? Color, double? Width, List<PointD>? Points);

public sealed record Result<T>(bool Ok, T? Value, string? Error)
{
    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Fail(string error) => new(false, default, error);
}