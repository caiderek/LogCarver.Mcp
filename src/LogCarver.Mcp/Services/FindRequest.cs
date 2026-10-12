using System.Globalization;
using System.Text.RegularExpressions;

namespace LogCarver.Mcp.Services;

public enum OperationFilter { Delete, Update, Insert, All }

public sealed record FindRequest(
    string Table,
    string? Database,
    OperationFilter Operations,
    DateTime? From,
    DateTime? To,
    string? KeyColumn,
    string? KeyValue,
    int MaxRows,
    bool IncludeUndoSql,
    IReadOnlyList<string> Warnings);

/// <summary>Raised for bad tool arguments or an unusable table, so the caller sees a clear message instead of a stack trace.</summary>
public sealed class ToolInputException(string message) : Exception(message);

public static partial class FindRequestParser
{
    public const int DefaultMaxRows = 20;
    public const int MaxRowsLimit = 500;

    private const string NamePart = @"(?:\[[^\[\]\.\x00-\x1F]{1,128}\]|[\p{L}_][\p{L}\p{N}_@#$]{0,127})";

    [GeneratedRegex("^" + NamePart + @"\." + NamePart + "$")]
    private static partial Regex QualifiedTableName();

    public static FindRequest Parse(
        string? table, string? database, string? operations, string? from, string? to,
        string? key, int maxRows, bool includeUndoSql)
    {
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(table))
            throw new ToolInputException("'table' is required, schema-qualified, e.g. 'dbo.Orders'.");
        table = table.Trim();
        if (!QualifiedTableName().IsMatch(table))
            throw new ToolInputException(
                $"'{table}' is not a valid schema-qualified table name. Use 'schema.table', e.g. 'dbo.Orders' or '[dbo].[Order Items]'.");

        if (database is not null)
        {
            database = database.Trim();
            if (database.Length == 0)
                database = null;
            else if (database.Length > 128 || database.Any(char.IsControl))
                throw new ToolInputException("'database' is not a valid database name.");
        }

        var filter = (operations?.Trim().ToLowerInvariant()) switch
        {
            null or "" or "delete" => OperationFilter.Delete,
            "update" => OperationFilter.Update,
            "insert" => OperationFilter.Insert,
            "all" => OperationFilter.All,
            _ => throw new ToolInputException($"'operations' must be one of: delete, update, insert, all (got '{operations}')."),
        };

        DateTime? fromTime = ParseTime(from, "from");
        DateTime? toTime = ParseTime(to, "to");
        if (fromTime is not null && toTime is not null && fromTime > toTime)
            throw new ToolInputException("'from' is later than 'to'.");

        string? keyColumn = null, keyValue = null;
        if (!string.IsNullOrWhiteSpace(key))
        {
            int eq = key.IndexOf('=');
            if (eq <= 0)
                throw new ToolInputException("'key' must look like 'Column=Value', e.g. 'Id=5'.");
            keyColumn = key[..eq].Trim();
            keyValue = key[(eq + 1)..].Trim();
            if (keyColumn.Length == 0)
                throw new ToolInputException("'key' must look like 'Column=Value', e.g. 'Id=5'.");
        }

        if (maxRows < 0)
            throw new ToolInputException("'maxRows' cannot be negative (use 0 to get counts only).");
        if (maxRows > MaxRowsLimit)
        {
            warnings.Add($"maxRows was capped at {MaxRowsLimit} (requested {maxRows}). Narrow the request with from/to or key to see more.");
            maxRows = MaxRowsLimit;
        }

        return new FindRequest(table, database, filter, fromTime, toTime, keyColumn, keyValue, maxRows, includeUndoSql, warnings);
    }

    private static DateTime? ParseTime(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        // fn_dblog timestamps are the server's local time and no zone
        // conversion is attempted: take the clock reading exactly as written,
        // ignoring any "Z" or offset suffix (DateTimeOffset.DateTime does that;
        // parsing straight to DateTime would convert offsets to this host's zone).
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            throw new ToolInputException($"'{name}' is not a valid date/time (got '{value}'). Use ISO 8601, e.g. '2026-10-12T09:30:00'.");
        return parsed.DateTime;
    }
}
