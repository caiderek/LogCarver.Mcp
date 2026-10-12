using LogCarver.Core.SqlServer;
using Microsoft.Data.SqlClient;

namespace LogCarver.Mcp.Services;

public sealed record ServerSettings(string ConnectionString);

public sealed class RowHistoryService(ServerSettings settings)
{
    /// <summary>
    /// Mirrors the LogCarver CLI's default listing path: reconstruct over the
    /// table's full observed history, then filter what is reported. Only
    /// SELECTs are issued - nothing is ever written to the server.
    /// </summary>
    public async Task<FindResult> FindAsync(FindRequest request, CancellationToken ct)
    {
        var builder = new SqlConnectionStringBuilder(settings.ConnectionString);
        if (request.Database is not null)
            builder.InitialCatalog = request.Database;
        builder.ApplicationName = "LogCarver.Mcp";

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(ct);

        var version = await SqlServerVersion.CheckAsync(connection, ct);

        var schema = await SchemaReader.GetTableSchemaAsync(connection, request.Table, ct);
        if (schema.Count == 0)
            throw new ToolInputException(
                $"No table '{request.Table}' found in database '{builder.InitialCatalog}'. " +
                "Check the name and that it includes the schema, e.g. 'dbo.Orders' rather than 'Orders'.");

        var compression = await CompressionChecker.GetCompressionTypesAsync(connection, request.Table, ct);
        if (CompressionChecker.IsCompressed(compression))
            throw new ToolInputException(
                $"'{request.Table}' uses {string.Join("/", compression.Where(t => t != "NONE"))} compression. " +
                "Compressed rows use a different physical layout that LogCarver does not decode yet; refusing rather than guessing.");

        var boundaries = await DdlBoundaryReader.GetDdlBoundariesAsync(connection, request.Table, ct);
        var transactionTimes = await TransactionTimeReader.GetTransactionBeginTimesAsync(connection, ct);
        var records = await FnDblogReader.ReadClusteredRecordsAsync(connection, request.Table, ct);

        var history = RowHistoryReconstructor.Reconstruct(
            records, schema, boundaries.Select(b => b.Lsn).ToList(), transactionTimes);

        var context = new HistoryContext(
            request.Table, builder.InitialCatalog, version.ProductVersion, version.IsValidated, boundaries.Count,
            boundaries.Select(b => b.TransactionName ?? "unnamed transaction").Distinct().Take(5).ToList());
        return ResultBuilder.Build(history, request, context);
    }
}
