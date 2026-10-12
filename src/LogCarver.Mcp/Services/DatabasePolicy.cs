namespace LogCarver.Mcp.Services;

/// <summary>
/// The tool's login is sysadmin, and the LLM calling it is untrusted, so the
/// operator decides which databases it may read. The database in the
/// connection string is always allowed; any other must be listed in
/// LOGCARVER_MCP_ALLOWED_DATABASES.
/// </summary>
public static class DatabasePolicy
{
    public static IReadOnlySet<string> ParseAllowList(string? raw) =>
        (raw ?? "")
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static void Check(string? configuredDatabase, IReadOnlySet<string> allowed, string? requested)
    {
        if (string.IsNullOrEmpty(requested))
            return;
        if (string.Equals(requested, configuredDatabase, StringComparison.OrdinalIgnoreCase))
            return;
        if (allowed.Contains(requested))
            return;

        throw new ToolInputException(
            $"Database '{requested}' is not permitted. This server may read " +
            (string.IsNullOrEmpty(configuredDatabase) ? "only the login's default database" : $"'{configuredDatabase}'") +
            (allowed.Count > 0 ? $" and: {string.Join(", ", allowed)}" : "") +
            ". The operator can allow more by listing them in LOGCARVER_MCP_ALLOWED_DATABASES.");
    }
}
