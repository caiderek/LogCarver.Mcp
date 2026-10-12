using LogCarver.Core.SqlServer;
using LogCarver.Mcp.Services;

namespace LogCarver.Mcp.Tests;

/// <summary>Regression tests for issues found by the independent review of the first build.</summary>
public class ReviewFindingsTests
{
    private static readonly HistoryContext Context = new("dbo.T", "Db", "17.0.1000.7", true, 0);

    private static FindRequest Parse(string? table = "dbo.Orders", string? from = null, string? to = null) =>
        FindRequestParser.Parse(table, null, null, from, to, null, 20, false);

    [Theory]
    [InlineData("2026-10-12T09:30:00+08:00")]
    [InlineData("2026-10-12T09:30:00-05:00")]
    [InlineData("2026-10-12T09:30:00Z")]
    [InlineData("2026-10-12T09:30:00")]
    public void TimeOffsets_AreIgnored_ClockReadingKeptAsWritten(string text) =>
        Assert.Equal(new DateTime(2026, 10, 12, 9, 30, 0), Parse(from: text).From);

    [Theory]
    [InlineData("[my.schema].[T]")]
    [InlineData("dbo.[a.b]")]
    public void DotInsideBrackets_IsRejected_BecauseTheUndoSqlBuilderSplitsOnDots(string table) =>
        Assert.Throws<ToolInputException>(() => Parse(table: table));

    [Fact]
    public void LoneSurrogateInAString_DoesNotBreakSerialization()
    {
        var history = new[]
        {
            Events.Make(RowEventKind.Delete, 1, before: new() { ["Name"] = "bad\uD800text" }),
        };

        var result = ResultBuilder.Build(history, Events.Request(), Context);
        string json = ResultBuilder.ToJson(result);

        Assert.Contains("bad", json);
    }

    [Fact]
    public void UnresolvableNote_CountsOnlyEventsOfTheRequestedKind()
    {
        var history = new[]
        {
            Events.Make(RowEventKind.Insert, 1, note: "could not decode"),
            Events.Make(RowEventKind.Insert, 2, note: "could not decode"),
            Events.Make(RowEventKind.Delete, 3, before: new() { ["Id"] = 7 }),
        };

        var result = ResultBuilder.Build(history, Events.Request(keyColumn: "Id", keyValue: "7"), Context);

        Assert.DoesNotContain(result.Summary.Notes, n => n.Contains("could not be decoded"));
    }

    [Fact]
    public void ZeroMaxRows_SaysCountsOnly_NotShowingZeroMostRecent()
    {
        var history = new[] { Events.Make(RowEventKind.Delete, 1, before: new() { ["Id"] = 1 }) };

        var result = ResultBuilder.Build(history, Events.Request(maxRows: 0), Context);

        Assert.DoesNotContain(result.Summary.Notes, n => n.Contains("0 most recent"));
        Assert.Contains(result.Summary.Notes, n => n.Contains("maxRows=0"));
    }
}
