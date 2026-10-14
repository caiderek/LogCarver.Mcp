using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using LogCarver.Core.SqlServer;
using LogCarver.Mcp.Output;

namespace LogCarver.Mcp.Services;

public sealed record HistoryContext(
    string Table, string? Database, string SqlServerVersion, bool VersionValidated, int DdlBoundaryCount,
    IReadOnlyList<string>? DdlBoundaryNames = null,
    IReadOnlyList<string>? KeyColumns = null);

public sealed record FindSummary(
    string Table,
    string? Database,
    string SqlServerVersion,
    int EventsInLog,
    IReadOnlyDictionary<string, int> EventsInLogByKind,
    int Matched,
    int Returned,
    bool Truncated,
    int NeedsManualReview,
    IReadOnlyList<string> Notes);

public sealed record FindEventDto(
    string Kind,
    string? Timestamp,
    string Lsn,
    string? TransactionId,
    Dictionary<string, object?>? Before,
    Dictionary<string, object?>? After,
    string? Note,
    string? UndoSql);

public sealed record FindResult(FindSummary Summary, IReadOnlyList<FindEventDto> Events);

public static class ResultBuilder
{
    /// <summary>Soft budget for the serialized events; one oversized event is still returned on its own.</summary>
    public const int MaxResponseChars = 100_000;
    public const int MaxUndoSqlChars = 20_000;

    private const string OfflineNote =
        "fn_dblog returned nothing for this table, which usually means the relevant log space has already been reused. " +
        "The bytes may still be physically in the .ldf file: the paid LogCarverOffline tool reads the raw .ldf and can often recover this case (https://github.com/caiderek/LogCarver).";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static FindResult Build(IReadOnlyList<RowEvent> history, FindRequest request, HistoryContext context)
    {
        var selected = EventSelector.Select(history, request);

        // Most recent events are the useful ones when the list is cut, so
        // keep the tail and (below) drop the oldest first on a size cut.
        var window = selected.Count > request.MaxRows ? selected.Skip(selected.Count - request.MaxRows).ToList() : selected.ToList();

        var dtos = new List<FindEventDto>(window.Count);
        int chars = 0;
        bool cutBySize = false;
        for (int i = window.Count - 1; i >= 0; i--)
        {
            var dto = ToDto(window[i], request, context.Table, context.KeyColumns);
            int size = JsonSerializer.Serialize(dto, JsonOptions).Length;
            if (dtos.Count > 0 && chars + size > MaxResponseChars)
            {
                cutBySize = true;
                break;
            }
            chars += size;
            dtos.Add(dto);
        }
        dtos.Reverse();

        bool truncated = dtos.Count < selected.Count;
        int needsReview = selected.Count(e => e.NeedsManualReview);

        var notes = new List<string>(request.Warnings);
        if (!context.VersionValidated)
            notes.Add($"SQL Server {context.SqlServerVersion} has not been validated against LogCarver; decoded values may be wrong without any error.");
        if (history.Count == 0)
            notes.Add(OfflineNote);
        if (context.DdlBoundaryCount > 0)
        {
            string names = context.DdlBoundaryNames is { Count: > 0 } n ? $" ({string.Join(", ", n)})" : "";
            notes.Add(
                $"{context.DdlBoundaryCount} schema-changing DDL boundary(ies) found for this table{names}. Events older than the newest boundary are refused rather than guessed. " +
                "A boundary that is only the table's own CREATE TABLE hides nothing older; TRUNCATE TABLE also counts as a boundary even though it does not change the columns.");
        }
        if (request.KeyColumn is not null)
        {
            // Only events that pass the kind/time filters could have matched the key.
            var withoutKey = EventSelector.Select(history, request with { KeyColumn = null, KeyValue = null });
            int unresolvable = withoutKey.Count(e => e.Before is null && e.After is null);
            if (unresolvable > 0)
                notes.Add($"{unresolvable} other event(s) could not be decoded and so were not checked against the key filter - the row you want may be among them.");
        }
        if (needsReview > 0)
            notes.Add($"{needsReview} matched event(s) are flagged needsManualReview; read each one's note before relying on it.");
        if (truncated)
        {
            notes.Add(cutBySize
                ? $"Output was cut at about {MaxResponseChars} characters; showing the {dtos.Count} most recent of {selected.Count} matches. Narrow with from/to or key."
                : request.MaxRows == 0
                    ? $"maxRows=0 returns counts only; {selected.Count} matching event(s) not shown. Raise maxRows to see them."
                    : $"Showing the {dtos.Count} most recent of {selected.Count} matches (maxRows={request.MaxRows}). Raise maxRows or narrow with from/to or key.");
        }

        var byKind = history.GroupBy(e => KindName(e.Kind)).ToDictionary(g => g.Key, g => g.Count());

        var summary = new FindSummary(
            context.Table, context.Database, context.SqlServerVersion,
            history.Count, byKind, selected.Count, dtos.Count, truncated, needsReview, notes);
        return new FindResult(summary, dtos);
    }

    public static string ToJson(FindResult result) => JsonSerializer.Serialize(result, JsonOptions);

    private static FindEventDto ToDto(RowEvent e, FindRequest request, string table, IReadOnlyList<string>? keyColumns)
    {
        string? undo = null;
        string? note = e.Note;
        if (request.IncludeUndoSql)
        {
            undo = UndoSqlGenerator.Generate(e, table, keyColumns: keyColumns);
            if (undo is not null && undo.Length > MaxUndoSqlChars)
            {
                note = (note is null ? "" : note + "; ") + $"undo SQL omitted ({undo.Length} characters is too large to return inline)";
                undo = null;
            }
        }

        return new FindEventDto(
            KindName(e.Kind),
            e.Timestamp?.ToString("yyyy-MM-dd'T'HH:mm:ss.fff"),
            e.Lsn,
            e.TransactionId,
            ValueJson.ConvertRow(e.Before),
            ValueJson.ConvertRow(e.After),
            note,
            undo);
    }

    private static string KindName(RowEventKind kind) => kind switch
    {
        RowEventKind.Insert => "insert",
        RowEventKind.Delete => "delete",
        RowEventKind.Update => "update",
        _ => kind.ToString().ToLowerInvariant(),
    };
}
