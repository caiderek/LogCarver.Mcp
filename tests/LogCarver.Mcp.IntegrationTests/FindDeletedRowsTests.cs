using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace LogCarver.Mcp.IntegrationTests;

public class FindDeletedRowsTests(ServerFixture fixture) : IClassFixture<ServerFixture>
{
    private async Task<CallToolResult> CallAsync(Dictionary<string, object?> args) =>
        await fixture.Client.CallToolAsync("find_deleted_rows", args);

    private static string TextOf(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));

    private async Task<JsonElement> OkAsync(Dictionary<string, object?> args)
    {
        var result = await CallAsync(args);
        Assert.NotEqual(true, result.IsError);
        return JsonDocument.Parse(TextOf(result)).RootElement;
    }

    [Fact]
    public async Task ToolIsListed_AsReadOnly()
    {
        var tools = await fixture.Client.ListToolsAsync();
        var tool = Assert.Single(tools, t => t.Name == "find_deleted_rows");
        Assert.Equal(true, tool.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.Equal(false, tool.ProtocolTool.Annotations?.DestructiveHint);
    }

    [Fact]
    public async Task Default_ReturnsBothDeletedRows_DecodedCorrectly()
    {
        var root = await OkAsync(new() { ["table"] = "dbo.Orders" });

        var events = root.GetProperty("events").EnumerateArray().ToList();
        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.Equal("delete", e.GetProperty("kind").GetString()));

        var ids = events.Select(e => e.GetProperty("before").GetProperty("Id").GetInt32()).ToArray();
        Assert.Equal(new[] { 2, 4, 5 }, ids);

        var two = events[0].GetProperty("before");
        Assert.Equal("訂單二", two.GetProperty("Name").GetString());
        Assert.Equal("20.00", two.GetProperty("Amount").GetString());
        Assert.Equal(JsonValueKind.Null, two.GetProperty("Payload").ValueKind);
        Assert.StartsWith("2026-10-02T09:15:30.456", two.GetProperty("CreatedAt").GetString());

        var five = events[2].GetProperty("before");
        Assert.Equal("0xFF00", five.GetProperty("Payload").GetString());
        Assert.Equal("50.75", five.GetProperty("Amount").GetString());

        var summary = root.GetProperty("summary");
        Assert.Equal(3, summary.GetProperty("matched").GetInt32());
        Assert.False(summary.GetProperty("truncated").GetBoolean());
        Assert.Equal(ServerFixture.DatabaseName, summary.GetProperty("database").GetString());
    }

    [Fact]
    public async Task OperationsAll_IncludesInsertsAndTheUpdateWithBothImages()
    {
        var root = await OkAsync(new() { ["table"] = "dbo.Orders", ["operations"] = "all", ["maxRows"] = 100 });

        var events = root.GetProperty("events").EnumerateArray().ToList();
        Assert.Equal(5, events.Count(e => e.GetProperty("kind").GetString() == "insert"));
        Assert.Equal(3, events.Count(e => e.GetProperty("kind").GetString() == "delete"));

        var update = Assert.Single(events, e => e.GetProperty("kind").GetString() == "update");
        Assert.Equal("gamma", update.GetProperty("before").GetProperty("Name").GetString());
        Assert.Equal("GAMMA", update.GetProperty("after").GetProperty("Name").GetString());
    }

    [Fact]
    public async Task KeyFilter_ReturnsOnlyThatRow()
    {
        var root = await OkAsync(new() { ["table"] = "dbo.Orders", ["key"] = "Id=4" });

        var only = Assert.Single(root.GetProperty("events").EnumerateArray());
        Assert.Equal("delta", only.GetProperty("before").GetProperty("Name").GetString());
    }

    [Fact]
    public async Task MaxRowsZero_ReturnsCountsOnly()
    {
        var root = await OkAsync(new() { ["table"] = "dbo.Orders", ["maxRows"] = 0 });

        Assert.Empty(root.GetProperty("events").EnumerateArray());
        Assert.Equal(3, root.GetProperty("summary").GetProperty("matched").GetInt32());
    }

    [Fact]
    public async Task MaxRowsOne_KeepsTheNewestDelete_AndSaysTruncated()
    {
        var root = await OkAsync(new() { ["table"] = "dbo.Orders", ["maxRows"] = 1 });

        var only = Assert.Single(root.GetProperty("events").EnumerateArray());
        Assert.Equal(5, only.GetProperty("before").GetProperty("Id").GetInt32());
        Assert.True(root.GetProperty("summary").GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task TimeWindowInTheFuture_MatchesNothing()
    {
        var root = await OkAsync(new() { ["table"] = "dbo.Orders", ["from"] = "2999-01-01T00:00:00" });

        Assert.Equal(0, root.GetProperty("summary").GetProperty("matched").GetInt32());
    }

    [Fact]
    public async Task IncludeUndoSql_ReturnsAnInsertForTheDeletedRow_AndNothingIsExecuted()
    {
        var root = await OkAsync(new() { ["table"] = "dbo.Orders", ["key"] = "Id=4", ["includeUndoSql"] = true });

        string undo = root.GetProperty("events")[0].GetProperty("undoSql").GetString()!;
        Assert.Contains("INSERT", undo, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("delta", undo);

        // The suggested SQL must stay a suggestion: the row is still deleted.
        var again = await OkAsync(new() { ["table"] = "dbo.Orders", ["key"] = "Id=4" });
        Assert.Single(again.GetProperty("events").EnumerateArray());
    }

    [Fact]
    public async Task MissingTable_IsAToolError_WithAClearMessage()
    {
        var result = await CallAsync(new() { ["table"] = "dbo.DoesNotExist" });

        Assert.True(result.IsError);
        Assert.Contains("No table 'dbo.DoesNotExist'", TextOf(result));
    }

    [Fact]
    public async Task InjectionShapedTableName_IsRejectedBeforeAnySqlRuns()
    {
        var result = await CallAsync(new() { ["table"] = "dbo.Orders; DROP TABLE dbo.Orders" });

        Assert.True(result.IsError);
        Assert.Contains("not a valid schema-qualified table name", TextOf(result));
        var stillThere = await OkAsync(new() { ["table"] = "dbo.Orders" });
        Assert.Equal(3, stillThere.GetProperty("summary").GetProperty("matched").GetInt32());
    }
}
