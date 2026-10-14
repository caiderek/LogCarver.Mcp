<p align="center"><img src="https://raw.githubusercontent.com/caiderek/LogCarver/master/docs/assets/logo.png" width="96" alt="LogCarver logo"></p>

<!-- mcp-name: io.github.caiderek/logcarver-mcp -->

# LogCarver.Mcp

**English | [繁體中文](https://github.com/caiderek/LogCarver.Mcp/blob/master/README.zh-TW.md)**

A read-only [MCP](https://modelcontextprotocol.io) server that lets an AI assistant answer "what rows were just deleted from this SQL Server table?" by reading the database's transaction log (`fn_dblog`). No Audit, CDC or Change Tracking needs to be enabled beforehand.

It is a thin wrapper over the decoding engine from [LogCarver](https://github.com/caiderek/LogCarver) (MIT). Everything runs on your machine and nothing is sent anywhere.

> **Status:** early (v0.1). On [NuGet](https://www.nuget.org/packages/LogCarver.Mcp) and in the official MCP registry as `io.github.caiderek/logcarver-mcp`.

## Quick start (5 minutes)

1. **Install** (needs the .NET 10 SDK or runtime):

   ```
   dotnet tool install -g LogCarver.Mcp
   ```

2. **Make a throwaway database.** Run [`examples/try-it.sql`](examples/try-it.sql) in SSMS or `sqlcmd`. It creates `McpTest`, inserts 10 orders, deletes 4 and updates 1.

3. **Keep the password out of the command.** Put the connection string in a file that only you can read (the SQL login must be sysadmin):

   ```
   Server=localhost;Database=McpTest;User Id=YOUR_LOGIN;Password=YOUR_PASSWORD;TrustServerCertificate=true
   ```

   Save it as, for example, `C:\Users\you\logcarver-mcp.conn` (one line, nothing else). Then add the server and point it at the file:

   ```
   claude mcp add --scope user logcarver --env LOGCARVER_MCP_CONNECTION_STRING_FILE="C:\Users\you\logcarver-mcp.conn" -- logcarver-mcp
   ```

   The password is never typed on the command line and never appears in the MCP configuration. Do not commit this file anywhere.

4. **Ask in a new conversation:** "Which rows were deleted from dbo.Orders in McpTest?" You should see Bob, Carol, Dave and Eve (Id 2 to 5). Then try "Show the history of Id=1" (Amount 100.00 to 111.11, Note first to changed) and "Include the undo SQL".

5. **Clean up:** `DROP DATABASE McpTest;`, `claude mcp remove logcarver`.

If the result is empty, the log space was probably reused (for example after a `CHECKPOINT` under SIMPLE recovery, or a log backup). See the limits below.

## What it does

One tool, `find_deleted_rows`. It only reads: `SELECT`s, and `DBCC PAGE` reads of data pages to rebuild rows whose INSERT has left the log. It never changes data, and the optional undo SQL is plain text that the tool never executes.

| Parameter | Meaning |
|---|---|
| `table` (required) | Schema-qualified, e.g. `dbo.Orders` |
| `database` | Defaults to the database in the connection string. Any other database is refused unless listed in `LOGCARVER_MCP_ALLOWED_DATABASES` |
| `operations` | `delete` (default), `update`, `insert`, `all` |
| `from`, `to` | ISO 8601 time window, server-local time, no zone conversion |
| `key` | `Column=Value`, return one row's history only. Column name is case-insensitive; dates, bit, decimals and hex binary match as you would write them |
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

The login is sysadmin and the caller is an AI, so the tool may only read the connection string's database. To let it read others, list them (comma or semicolon separated):

```
LOGCARVER_MCP_ALLOWED_DATABASES=Sales;HR
```

Row values come straight from the database. They are data, not instructions, and the tool description tells the model so.

### Install

Needs the .NET 10 SDK or runtime.

```
dotnet tool install -g LogCarver.Mcp
```

Or run it without installing, through `dnx`:

```
dotnet dnx LogCarver.Mcp --yes
```

### Claude Code

```
claude mcp add logcarver --env LOGCARVER_MCP_CONNECTION_STRING="Server=localhost;Database=MyDb;Integrated Security=true;TrustServerCertificate=true" -- logcarver-mcp
```

With `dnx` instead of a global tool, end the command with `-- dotnet dnx LogCarver.Mcp --yes`.

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

MIT, see [LICENSE](https://github.com/caiderek/LogCarver.Mcp/blob/master/LICENSE).
