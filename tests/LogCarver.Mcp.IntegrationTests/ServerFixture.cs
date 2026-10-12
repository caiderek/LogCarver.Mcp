using Microsoft.Data.SqlClient;
using ModelContextProtocol.Client;

namespace LogCarver.Mcp.IntegrationTests;

/// <summary>
/// Real SQL Server + the real server process over stdio. The sandbox
/// database always carries the "LogCarver_" prefix (the same rule the other
/// LogCarver repos enforce) so this can never touch an unrelated database.
/// </summary>
public sealed class ServerFixture : IAsyncLifetime
{
    public const string DatabaseName = "LogCarver_McpTests";
    private const string MasterConnection = "Server=localhost;Database=master;Integrated Security=true;TrustServerCertificate=true";

    public static string DatabaseConnection => $"Server=localhost;Database={DatabaseName};Integrated Security=true;TrustServerCertificate=true";

    public McpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        AssertSafeName(DatabaseName);
        await DropAsync();

        await ExecAsync(MasterConnection, $"CREATE DATABASE [{DatabaseName}]");
        await ExecAsync(MasterConnection, $"ALTER DATABASE [{DatabaseName}] SET RECOVERY FULL");
        // First full backup is what makes FULL recovery keep the log instead of recycling it.
        await ExecAsync(MasterConnection, $"BACKUP DATABASE [{DatabaseName}] TO DISK = 'NUL:'");

        await ExecAsync(DatabaseConnection, """
            CREATE TABLE dbo.Orders (
                Id        INT           NOT NULL PRIMARY KEY CLUSTERED,
                Name      NVARCHAR(50)  NOT NULL,
                Amount    DECIMAL(10,2) NOT NULL,
                Payload   VARBINARY(16) NULL,
                CreatedAt DATETIME2(3)  NOT NULL
            );
            INSERT dbo.Orders (Id, Name, Amount, Payload, CreatedAt) VALUES
                (1, N'alpha',  10.50, 0x0102, '2026-10-01T08:00:00.123'),
                (2, N'訂單二',  20.00, NULL,   '2026-10-02T09:15:30.456'),
                (3, N'gamma',  30.25, 0xAB,   '2026-10-03T10:00:00.000'),
                (4, N'delta',  40.00, NULL,   '2026-10-04T11:11:11.111'),
                (5, N'epsilon',50.75, 0xFF00, '2026-10-05T12:00:00.000');
            UPDATE dbo.Orders SET Name = N'GAMMA' WHERE Id = 3;
            DELETE dbo.Orders WHERE Id IN (2, 4, 5);

            CREATE TABLE dbo.Documents (Id INT NOT NULL PRIMARY KEY, Body XML NULL);
            INSERT dbo.Documents VALUES (1, N'<doc><title>draft</title></doc>');
            DELETE dbo.Documents WHERE Id = 1;
            """);

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "logcarver-mcp-under-test",
            Command = "dotnet",
            Arguments = [ServerDllPath()],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["LOGCARVER_MCP_CONNECTION_STRING"] = DatabaseConnection,
            },
        });
        Client = await McpClient.CreateAsync(transport);
    }

    public async Task DisposeAsync()
    {
        if (Client is not null)
            await Client.DisposeAsync();
        await DropAsync();
    }

    private static async Task DropAsync()
    {
        AssertSafeName(DatabaseName);
        await ExecAsync(MasterConnection, $"""
            IF DB_ID(N'{DatabaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{DatabaseName}];
            END
            """);
    }

    private static async Task ExecAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }

    private static void AssertSafeName(string name)
    {
        if (!name.StartsWith("LogCarver_", StringComparison.Ordinal))
            throw new InvalidOperationException($"Refusing to touch database '{name}': test databases must start with 'LogCarver_'.");
    }

    private static string ServerDllPath()
    {
        // .../tests/LogCarver.Mcp.IntegrationTests/bin/<Config>/net10.0/
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string config = dir.Parent!.Name;
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LogCarver.Mcp.slnx")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("Could not find the repository root (LogCarver.Mcp.slnx).");

        string path = Path.Combine(dir.FullName, "src", "LogCarver.Mcp", "bin", config, "net10.0", "LogCarver.Mcp.dll");
        if (!File.Exists(path))
            throw new FileNotFoundException("Server build output not found.", path);
        return path;
    }
}
