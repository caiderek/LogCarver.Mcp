<p align="center"><img src="LogCarver/docs/assets/logo.png" width="96" alt="LogCarver logo"></p>

# LogCarver.Mcp

**English | [繁體中文](README.zh-TW.md)**

A read-only [MCP](https://modelcontextprotocol.io) server that lets an AI assistant answer "what rows were just deleted from this SQL Server table?" by reading the database's transaction log (`fn_dblog`). No Audit, CDC or Change Tracking needs to be enabled beforehand.

It is a thin wrapper over the decoding engine from [LogCarver](https://github.com/caiderek/LogCarver) (MIT). Everything runs on your machine and nothing is sent anywhere.

> **Status:** early (v0.1). Not published to NuGet or the MCP registry yet; build from source for now.

## What it does

One tool, `find_deleted_rows`. It only runs `SELECT`s. It never changes data, and the optional undo SQL is plain text that the tool never executes.

| Parameter | Meaning |
|---|---|
| `table` (required) | Schema-qualified, e.g. `dbo.Orders` |
| `database` | Defaults to the database in the connection string |
| `operations` | `delete` (default), `update`, `insert`, `all` |
| `from`, `to` | ISO 8601 time window, server-local time, no zone conversion |
| `key` | `Column=Value`, return one row's history only |
| `maxRows` | Default 20, max 500, `0` = counts only. The most recent events are kept |
| `includeUndoSql` | Also return suggested SQL that would reverse each event |

The result is JSON: a `summary` (counts, truncation, warnings and notes) and `events` (kind, timestamp, LSN, transaction id, before/after column values, note). Binary values are hex strings and decimals are strings, so nothing is silently rounded.

## Requirements and limits

- The connection's login must be **sysadmin** (`fn_dblog` requires it).
- You only see what is still in the **active log**. If the log space has been reused the result is empty, and that is not a bug. The paid LogCarverOffline tool can often recover that case by reading the raw `.ldf`.
- `TRUNCATE` and `DROP` cannot be recovered.
- Compressed tables are refused. `time` and `xml` columns and off-row LOB values are refused or flagged `needsManualReview`; they are never guessed.
- Validated on SQL Server 2025, 2022, 2019 and 2016 (SP2). Other versions return a warning in `notes`.
- Scanning a large active log can take a minute or more.

## Configure

The connection string comes from an environment variable, so credentials never pass through the AI conversation:

```
LOGCARVER_MCP_CONNECTION_STRING=Server=localhost;Database=MyDb;Integrated Security=true;TrustServerCertificate=true
```

Or point `LOGCARVER_MCP_CONNECTION_STRING_FILE` at a file that contains it.

### Claude Code

```
claude mcp add logcarver --env LOGCARVER_MCP_CONNECTION_STRING="Server=localhost;Database=MyDb;Integrated Security=true;TrustServerCertificate=true" -- logcarver-mcp
```

### From source

```
git clone --recurse-submodules https://github.com/caiderek/LogCarver.Mcp
dotnet build
dotnet run --project src/LogCarver.Mcp
```

## Tests

```
dotnet test tests/LogCarver.Mcp.Tests
dotnet test tests/LogCarver.Mcp.IntegrationTests
```

The integration tests need a local SQL Server and a sysadmin Windows login. They create and drop their own sandbox database, `LogCarver_McpTests`.

## License

MIT, see [LICENSE](LICENSE).
