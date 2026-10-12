using LogCarver.Core.SqlServer;
using LogCarver.Mcp.Services;

namespace LogCarver.Mcp.Tests;

internal static class Events
{
    public static RowEvent Make(
        RowEventKind kind, int seq, DateTime? timestamp = null,
        Dictionary<string, object?>? before = null, Dictionary<string, object?>? after = null, string? note = null) =>
        new($"0000002a:00000{seq:D3}:0001", kind, before, after, note, timestamp, "1:100", seq, $"0000:{seq:D8}");

    public static FindRequest Request(
        OperationFilter operations = OperationFilter.Delete, DateTime? from = null, DateTime? to = null,
        string? keyColumn = null, string? keyValue = null, int maxRows = 20, bool undo = false) =>
        new("dbo.T", null, operations, from, to, keyColumn, keyValue, maxRows, undo, []);
}

public class EventSelectorTests
{
    private static readonly DateTime T9 = new(2026, 10, 12, 9, 0, 0);
    private static readonly DateTime T10 = new(2026, 10, 12, 10, 0, 0);
    private static readonly DateTime T11 = new(2026, 10, 12, 11, 0, 0);

    [Fact]
    public void OperationFilter_SelectsOnlyThatKind()
    {
        var history = new[]
        {
            Events.Make(RowEventKind.Insert, 1, after: new() { ["Id"] = 1 }),
            Events.Make(RowEventKind.Update, 2, before: new() { ["Id"] = 1 }, after: new() { ["Id"] = 1 }),
            Events.Make(RowEventKind.Delete, 3, before: new() { ["Id"] = 1 }),
        };

        Assert.Single(EventSelector.Select(history, Events.Request(OperationFilter.Delete)));
        Assert.Equal(RowEventKind.Delete, EventSelector.Select(history, Events.Request(OperationFilter.Delete))[0].Kind);
        Assert.Equal(RowEventKind.Update, EventSelector.Select(history, Events.Request(OperationFilter.Update))[0].Kind);
        Assert.Equal(RowEventKind.Insert, EventSelector.Select(history, Events.Request(OperationFilter.Insert))[0].Kind);
        Assert.Equal(3, EventSelector.Select(history, Events.Request(OperationFilter.All)).Count);
    }

    [Fact]
    public void TimeWindow_IsInclusive_AndUnknownTimestampAlwaysPasses()
    {
        var history = new[]
        {
            Events.Make(RowEventKind.Delete, 1, T9, before: new() { ["Id"] = 1 }),
            Events.Make(RowEventKind.Delete, 2, T10, before: new() { ["Id"] = 2 }),
            Events.Make(RowEventKind.Delete, 3, T11, before: new() { ["Id"] = 3 }),
            Events.Make(RowEventKind.Delete, 4, null, before: new() { ["Id"] = 4 }),
        };

        var picked = EventSelector.Select(history, Events.Request(from: T10, to: T10));

        Assert.Equal(new[] { 2, 4 }, picked.Select(e => (int)e.Before!["Id"]!).ToArray());
    }

    [Fact]
    public void KeyFilter_MatchesBeforeOrAfterImage()
    {
        var history = new[]
        {
            Events.Make(RowEventKind.Delete, 1, before: new() { ["Id"] = 5 }),
            Events.Make(RowEventKind.Delete, 2, before: new() { ["Id"] = 6 }),
        };

        var picked = EventSelector.Select(history, Events.Request(keyColumn: "Id", keyValue: "5"));

        Assert.Single(picked);
        Assert.Equal(5, picked[0].Before!["Id"]);
    }
}
