using Newtin.Core.Models;

namespace Newtin.Core.Interfaces;

public interface IInfoProvider
{
    Task<IReadOnlyList<InfoSection>> CollectAsync(CancellationToken cancellationToken = default);
}