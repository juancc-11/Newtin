using Newtin.Core.Interfaces;
using Newtin.Core.Models;

namespace Newtin.Diagnostics;

public abstract class InfoProviderBase : IInfoProvider
{
    // Las consultas se ejecutan fuera del hilo de la interfaz para no congelar la ventana.
    public Task<IReadOnlyList<InfoSection>> CollectAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Collect(cancellationToken), cancellationToken);

    protected abstract IReadOnlyList<InfoSection> Collect(CancellationToken cancellationToken);
}