using Newtin.Core.Interfaces;
using Newtin.Core.Models;

namespace Newtin.Diagnostics.Events;

public sealed class RecentEventsProvider : InfoProviderBase
{
    private static readonly string[] Logs = ["System", "Application"];

    private readonly IEventLogReader _reader;
    private readonly EventQueryOptions _options;

    public RecentEventsProvider(IEventLogReader reader, EventQueryOptions options)
    {
        _reader = reader;
        _options = options;
    }

    protected override IReadOnlyList<InfoSection> Collect(CancellationToken cancellationToken)
    {
        var all = new List<EventSummary>();
        var problems = new List<InfoItem>();
        var truncated = false;

        foreach (var log in Logs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = _reader.Read(log, _options.Window, _options.IncludeWarnings,
                _options.MaxEventsPerLog, _options.MaxShown, cancellationToken);

            all.AddRange(result.Events);
            truncated |= result.Truncated;

            if (result.Error is not null)
                problems.Add(new InfoItem($"Registro {(log == "System" ? "Sistema" : "Aplicación")}",
                    InfoItem.NotAvailable, result.Error));
        }

        return EventFormatter.ToSections(all, truncated, problems, _options);
    }
}