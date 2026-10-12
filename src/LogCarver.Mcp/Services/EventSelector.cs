using LogCarver.Core.SqlServer;

namespace LogCarver.Mcp.Services;

public static class EventSelector
{
    /// <summary>
    /// Applies the request's filters to the table's FULL reconstructed history.
    /// Reconstruction must already have run over everything (an UPDATE's
    /// before-image can come from outside the window); this only chooses
    /// which events to report. An event with no resolvable timestamp always
    /// passes the time window, same as the CLI.
    /// </summary>
    public static IReadOnlyList<RowEvent> Select(IReadOnlyList<RowEvent> history, FindRequest request) =>
        history.Where(e =>
                KindMatches(e.Kind, request.Operations) &&
                (request.From is null || e.Timestamp is null || e.Timestamp >= request.From) &&
                (request.To is null || e.Timestamp is null || e.Timestamp <= request.To) &&
                (request.KeyColumn is null || RowEventFilter.MatchesKey(e, request.KeyColumn, request.KeyValue!)))
            .ToList();

    private static bool KindMatches(RowEventKind kind, OperationFilter filter) => filter switch
    {
        OperationFilter.All => true,
        OperationFilter.Delete => kind == RowEventKind.Delete,
        OperationFilter.Update => kind == RowEventKind.Update,
        OperationFilter.Insert => kind == RowEventKind.Insert,
        _ => false,
    };
}
