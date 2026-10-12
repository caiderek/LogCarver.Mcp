using LogCarver.Mcp;
using LogCarver.Mcp.Services;
using LogCarver.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// stdout is the MCP protocol channel - anything else written there corrupts it.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

string? connectionString = SecretConfig.Read(builder.Configuration, "LOGCARVER_MCP_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine(
        "LOGCARVER_MCP_CONNECTION_STRING is not set (or LOGCARVER_MCP_CONNECTION_STRING_FILE pointing at a file containing it). " +
        "Example: Server=localhost;Database=MyDb;Integrated Security=true;TrustServerCertificate=true");
    return 1;
}

var allowedDatabases = DatabasePolicy.ParseAllowList(builder.Configuration["LOGCARVER_MCP_ALLOWED_DATABASES"]);
builder.Services.AddSingleton(new ServerSettings(connectionString, allowedDatabases));
builder.Services.AddSingleton<RowHistoryService>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<FindDeletedRowsTool>();

await builder.Build().RunAsync();
return 0;
