using Microsoft.AspNetCore.SignalR;
using Newtin.Core.Interfaces;
using Newtin.Server.Models;
using Newtin.Server.Services;
using Newtin.Server.Validation;

namespace Newtin.Server.Hubs;

/// <summary>
/// Cliente -> servidor: Join, RequestState, CreateElement, UpdateElement, MoveElement, DeleteElement, ClearBoard,
///                      CreateCategory, UpdateCategory, DeleteCategory, StrokeProgress.
/// Servidor -> clientes: ElementCreated, ElementUpdated, ElementMoved, ElementDeleted, CategoryCreated, CategoryUpdated,
///                       CategoryDeleted, BoardCleared, StrokeProgress, ClientsChanged.
/// Los cambios se envían a TODOS (incluido quien los hizo) con el estado oficial; el cliente los aplica por Id y Version,
/// así un mismo evento nunca se duplica.
/// </summary>
public sealed class WhiteboardHub(WhiteboardStateService state, ClientSessionService sessions, ILogService log) : Hub
{
    public async Task<JoinResult> Join(string? displayName)
    {
        if (!WhiteboardValidator.TryNormalizeDisplayName(displayName, out var name, out var error))
            throw new HubException(error);

        sessions.Join(Context.ConnectionId, name);
        log.Info($"Cliente de la pizarra conectado: {name}.");

        var clients = sessions.Info();
        await Clients.Others.SendAsync("ClientsChanged", clients);
        return new JoinResult(state.Snapshot(), clients);
    }

    public WhiteboardSnapshot RequestState()
    {
        Admit();
        return state.Snapshot();
    }

    public async Task CreateElement(ElementInput? input)
    {
        var author = Admit();
        var element = Unwrap(state.CreateElement(input, author));
        await Clients.All.SendAsync("ElementCreated", element);
    }

    public async Task UpdateElement(ElementInput? input)
    {
        Admit();
        var element = Unwrap(state.UpdateElement(input));
        await Clients.All.SendAsync("ElementUpdated", element);
    }

    public async Task MoveElement(string? id, double x, double y)
    {
        Admit();
        var element = Unwrap(state.MoveElement(id, x, y));
        await Clients.All.SendAsync("ElementMoved", element);
    }

    public async Task DeleteElement(string? id)
    {
        Admit();
        var deletedId = Unwrap(state.DeleteElement(id));
        await Clients.All.SendAsync("ElementDeleted", deletedId);
    }

    public async Task ClearBoard()
    {
        var author = Admit();
        var removed = state.Clear();
        state.MarkChanged();
        log.Info($"Pizarra limpiada por {author} ({removed} elementos).");
        await Clients.All.SendAsync("BoardCleared");
    }

    public async Task CreateCategory(CategoryInput? input)
    {
        Admit();
        var category = Unwrap(state.CreateCategory(input));
        await Clients.All.SendAsync("CategoryCreated", category);
    }

    public async Task UpdateCategory(CategoryInput? input)
    {
        Admit();
        var category = Unwrap(state.UpdateCategory(input));
        await Clients.All.SendAsync("CategoryUpdated", category);
    }

    public async Task DeleteCategory(string? id)
    {
        Admit();
        var deletion = Unwrap(state.DeleteCategory(id));
        await Clients.All.SendAsync("CategoryDeleted", deletion.Id);
        foreach (var element in deletion.UpdatedElements)
            await Clients.All.SendAsync("ElementUpdated", element);
    }

    /// <summary>Vista previa de un trazo en curso: se reenvía a los demás y no se guarda.</summary>
    public async Task StrokeProgress(StrokeProgressInput? input)
    {
        var author = Admit();

        if (input is null) throw new HubException("Faltan los datos del trazo.");
        if (WhiteboardValidator.ValidateId(input.Id) is { } idError) throw new HubException(idError);
        if (WhiteboardValidator.ValidateColor(input.Color) is { } colorError) throw new HubException(colorError);
        if (WhiteboardValidator.ValidateWidth(input.Width) is { } widthError) throw new HubException(widthError);
        if (WhiteboardValidator.ValidatePoints(input.Points, WhiteboardLimits.MaxPointsPerProgress) is { } pointsError)
            throw new HubException(pointsError);

        await Clients.Others.SendAsync("StrokeProgress", new StrokeProgressEvent(
            input.Id!, input.Color!, input.Width ?? WhiteboardLimits.DefaultStrokeWidth, input.Points!, author));
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var name = sessions.NameOf(Context.ConnectionId);
        if (sessions.Remove(Context.ConnectionId))
        {
            log.Info($"Cliente de la pizarra desconectado: {name}.");
            await Clients.All.SendAsync("ClientsChanged", sessions.Info());
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Exige haberse identificado y respetar el límite de operaciones por segundo. Devuelve el nombre del autor.</summary>
    private string Admit()
    {
        var name = sessions.NameOf(Context.ConnectionId)
                   ?? throw new HubException("Primero debes identificarte con tu nombre.");

        if (!sessions.TryConsume(Context.ConnectionId, Environment.TickCount64))
            throw new HubException("Estás enviando demasiados cambios por segundo. Espera un momento.");

        return name;
    }

    private T Unwrap<T>(Result<T> result)
    {
        if (!result.Ok) throw new HubException(result.Error);
        state.MarkChanged();
        return result.Value!;
    }
}