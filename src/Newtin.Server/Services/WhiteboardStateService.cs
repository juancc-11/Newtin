using Newtin.Server.Models;
using Newtin.Server.Validation;
using static Newtin.Server.Validation.WhiteboardLimits;
using CategoryResult = Newtin.Server.Models.Result<Newtin.Server.Models.WhiteboardCategory>;
using DeletionResult = Newtin.Server.Models.Result<Newtin.Server.Models.CategoryDeletion>;
using ElementResult = Newtin.Server.Models.Result<Newtin.Server.Models.WhiteboardElement>;
using IdResult = Newtin.Server.Models.Result<string>;

namespace Newtin.Server.Services;

/// <summary>
/// Estado compartido en memoria. Las actualizaciones simultáneas se resuelven "gana la última":
/// cada cambio sube la versión del elemento y los clientes ignoran versiones más viejas.
/// </summary>
public sealed class WhiteboardStateService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, WhiteboardElement> _elements = new();
    private readonly Dictionary<string, WhiteboardCategory> _categories = new();
    private long _order;

    public int ElementCount
    {
        get { lock (_gate) return _elements.Count; }
    }

        public ElementResult CreateElement(ElementInput? input, string author)
    {
        if (input is null) return ElementResult.Fail("Faltan los datos del elemento.");
        if (input.Type is null) return ElementResult.Fail("Falta el tipo de elemento.");
        if (WhiteboardValidator.ValidateId(input.Id) is { } idError) return ElementResult.Fail(idError);
        if (WhiteboardValidator.ValidatePosition(input.X, input.Y) is { } positionError) return ElementResult.Fail(positionError);
        if (input.Color is not null && WhiteboardValidator.ValidateColor(input.Color) is { } colorError) return ElementResult.Fail(colorError);

        var categoryId = string.IsNullOrEmpty(input.CategoryId) ? null : input.CategoryId;
        if (categoryId is not null && WhiteboardValidator.ValidateId(categoryId) is { } categoryError) return ElementResult.Fail(categoryError);

        var type = input.Type.Value;
        string? text = null;
        PointD[]? points = null;
        double? width = null;

        if (type == ElementType.Note)
        {
            text = WhiteboardValidator.NormalizeText(input.Text);
            if (text.Length > MaxTextLength)
                return ElementResult.Fail($"La nota supera el máximo de {MaxTextLength} caracteres.");
        }
        else if (type == ElementType.Text)
        {
            // En un texto libre, Width es el tamaño de la letra.
            text = WhiteboardValidator.NormalizeText(input.Text).TrimEnd();
            if (text.Length == 0) return ElementResult.Fail("El texto no puede estar vacío.");
            if (text.Length > MaxTextLength)
                return ElementResult.Fail($"El texto supera el máximo de {MaxTextLength} caracteres.");
            if (WhiteboardValidator.ValidateFontSize(input.Width) is { } sizeError) return ElementResult.Fail(sizeError);

            width = input.Width ?? DefaultFontSize;
        }
        else
        {
            if (WhiteboardValidator.ValidatePoints(input.Points, MaxPointsPerStroke) is { } pointsError) return ElementResult.Fail(pointsError);
            if (WhiteboardValidator.ValidateWidth(input.Width) is { } widthError) return ElementResult.Fail(widthError);

            points = input.Points!.ToArray();
            width = input.Width ?? DefaultStrokeWidth;
        }

        lock (_gate)
        {
            if (_elements.ContainsKey(input.Id!)) return ElementResult.Fail("Ya existe un elemento con ese identificador.");
            if (_elements.Count >= MaxElements) return ElementResult.Fail("La pizarra alcanzó el máximo de elementos.");
            if (categoryId is not null && !_categories.ContainsKey(categoryId)) return ElementResult.Fail("La categoría no existe.");

            var now = DateTimeOffset.UtcNow;
            var element = new WhiteboardElement(
                input.Id!, type, input.X, input.Y, text, points,
                input.Color ?? DefaultColorFor(type),
                width, categoryId, author, now, now, 1, ++_order);

            _elements[element.Id] = element;
            return ElementResult.Success(element);
        }
    }

    /// <summary>Cambia texto (notas y textos libres), color, grosor o tamaño de letra y categoría. No mueve el elemento.</summary>
    public ElementResult UpdateElement(ElementInput? input)
    {
        if (input is null) return ElementResult.Fail("Faltan los datos del elemento.");
        if (WhiteboardValidator.ValidateId(input.Id) is { } idError) return ElementResult.Fail(idError);
        if (input.Color is not null && WhiteboardValidator.ValidateColor(input.Color) is { } colorError) return ElementResult.Fail(colorError);

        var categoryId = string.IsNullOrEmpty(input.CategoryId) ? null : input.CategoryId;
        if (categoryId is not null && WhiteboardValidator.ValidateId(categoryId) is { } categoryError) return ElementResult.Fail(categoryError);

        lock (_gate)
        {
            if (!_elements.TryGetValue(input.Id!, out var existing)) return ElementResult.Fail("El elemento no existe.");
            if (categoryId is not null && !_categories.ContainsKey(categoryId)) return ElementResult.Fail("La categoría no existe.");

            var text = existing.Text;
            if ((existing.Type == ElementType.Note || existing.Type == ElementType.Text) && input.Text is not null)
            {
                text = WhiteboardValidator.NormalizeText(input.Text);
                if (existing.Type == ElementType.Text)
                {
                    text = text.TrimEnd();
                    if (text.Length == 0) return ElementResult.Fail("El texto no puede estar vacío.");
                }

                if (text.Length > MaxTextLength)
                    return ElementResult.Fail($"El texto supera el máximo de {MaxTextLength} caracteres.");
            }

            var width = existing.Width;
            if (existing.Type != ElementType.Note && input.Width is not null)
            {
                var sizeError = existing.Type == ElementType.Text
                    ? WhiteboardValidator.ValidateFontSize(input.Width)
                    : WhiteboardValidator.ValidateWidth(input.Width);
                if (sizeError is not null) return ElementResult.Fail(sizeError);

                width = input.Width;
            }

            var updated = existing with
            {
                Text = text,
                Color = input.Color ?? existing.Color,
                Width = width,
                CategoryId = categoryId,
                ModifiedAt = DateTimeOffset.UtcNow,
                Version = existing.Version + 1
            };

            _elements[existing.Id] = updated;
            return ElementResult.Success(updated);
        }
    }

    private static string DefaultColorFor(ElementType type) => type switch
    {
        ElementType.Note => DefaultNoteColor,
        ElementType.Text => DefaultTextColor,
        _ => DefaultStrokeColor
    };

    public ElementResult MoveElement(string? id, double x, double y)
    {
        if (WhiteboardValidator.ValidateId(id) is { } idError) return ElementResult.Fail(idError);
        if (WhiteboardValidator.ValidatePosition(x, y) is { } positionError) return ElementResult.Fail(positionError);

        lock (_gate)
        {
            if (!_elements.TryGetValue(id!, out var existing)) return ElementResult.Fail("El elemento no existe.");

            var moved = existing with
            {
                X = x,
                Y = y,
                ModifiedAt = DateTimeOffset.UtcNow,
                Version = existing.Version + 1
            };

            _elements[existing.Id] = moved;
            return ElementResult.Success(moved);
        }
    }

    public IdResult DeleteElement(string? id)
    {
        if (WhiteboardValidator.ValidateId(id) is { } idError) return IdResult.Fail(idError);

        lock (_gate)
            return _elements.Remove(id!) ? IdResult.Success(id!) : IdResult.Fail("El elemento no existe.");
    }

    public int Clear()
    {
        lock (_gate)
        {
            var count = _elements.Count;
            _elements.Clear();
            return count;
        }
    }

    public CategoryResult CreateCategory(CategoryInput? input)
    {
        if (input is null) return CategoryResult.Fail("Faltan los datos de la categoría.");
        if (WhiteboardValidator.ValidateId(input.Id) is { } idError) return CategoryResult.Fail(idError);
        if (!WhiteboardValidator.TryNormalizeCategoryName(input.Name, out var name, out var nameError)) return CategoryResult.Fail(nameError);

        var color = input.Color ?? DefaultCategoryColor;
        if (WhiteboardValidator.ValidateColor(color) is { } colorError) return CategoryResult.Fail(colorError);

        lock (_gate)
        {
            if (_categories.ContainsKey(input.Id!)) return CategoryResult.Fail("Ya existe una categoría con ese identificador.");
            if (_categories.Count >= MaxCategories) return CategoryResult.Fail("Se alcanzó el máximo de categorías.");
            if (_categories.Values.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return CategoryResult.Fail("Ya existe una categoría con ese nombre.");

            var category = new WhiteboardCategory(input.Id!, name, color);
            _categories[category.Id] = category;
            return CategoryResult.Success(category);
        }
    }

    public CategoryResult UpdateCategory(CategoryInput? input)
    {
        if (input is null) return CategoryResult.Fail("Faltan los datos de la categoría.");
        if (WhiteboardValidator.ValidateId(input.Id) is { } idError) return CategoryResult.Fail(idError);
        if (input.Color is not null && WhiteboardValidator.ValidateColor(input.Color) is { } colorError) return CategoryResult.Fail(colorError);

        var newName = "";
        if (input.Name is not null
            && !WhiteboardValidator.TryNormalizeCategoryName(input.Name, out newName, out var nameError))
            return CategoryResult.Fail(nameError);

        lock (_gate)
        {
            if (!_categories.TryGetValue(input.Id!, out var existing)) return CategoryResult.Fail("La categoría no existe.");

            var name = input.Name is null ? existing.Name : newName;
            if (_categories.Values.Any(c => c.Id != existing.Id && c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return CategoryResult.Fail("Ya existe una categoría con ese nombre.");

            var updated = existing with { Name = name, Color = input.Color ?? existing.Color };
            _categories[existing.Id] = updated;
            return CategoryResult.Success(updated);
        }
    }

    public DeletionResult DeleteCategory(string? id)
    {
        if (WhiteboardValidator.ValidateId(id) is { } idError) return DeletionResult.Fail(idError);

        lock (_gate)
        {
            if (!_categories.Remove(id!)) return DeletionResult.Fail("La categoría no existe.");

            var now = DateTimeOffset.UtcNow;
            var updated = new List<WhiteboardElement>();
            foreach (var element in _elements.Values.Where(e => e.CategoryId == id).ToList())
            {
                var cleared = element with { CategoryId = null, ModifiedAt = now, Version = element.Version + 1 };
                _elements[element.Id] = cleared;
                updated.Add(cleared);
            }

            return DeletionResult.Success(new CategoryDeletion(id!, updated));
        }
    }

    public WhiteboardSnapshot Snapshot()
    {
        lock (_gate)
            return new WhiteboardSnapshot(
                _elements.Values.OrderBy(e => e.Order).ToArray(),
                _categories.Values.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToArray());
    }

        /// <summary>Se dispara cuando cambia el contenido. Quien modifica el estado llama a MarkChanged una vez por operación.</summary>
    public event Action? Changed;

    public void MarkChanged() => Changed?.Invoke();

    /// <summary>Quita todos los elementos y todas las categorías.</summary>
    public void ClearAll()
    {
        lock (_gate)
        {
            _elements.Clear();
            _categories.Clear();
        }
    }
}