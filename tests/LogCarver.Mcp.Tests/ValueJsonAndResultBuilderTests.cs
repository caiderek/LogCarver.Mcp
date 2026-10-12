using System.Numerics;
using System.Text.Json;
using LogCarver.Core;
using LogCarver.Core.SqlServer;
using LogCarver.Mcp.Output;
using LogCarver.Mcp.Services;

namespace LogCarver.Mcp.Tests;

public class ValueJsonTests
{
    [Fact]
    public void Binary_BecomesHex_NotTheTypeName()
    {
        Assert.Equal("0x0A0B", ValueJson.Convert(new byte[] { 0x0A, 0x0B }));
        Assert.Equal("0x", ValueJson.Convert(Array.Empty<byte>()));
    }

    [Fact]
    public void LongBinary_IsCut_AndSaysHowLong()
    {
        var text = (string)ValueJson.Convert(new byte[1000])!;
        Assert.StartsWith("0x" + new string('0', ValueJson.MaxBinaryBytesShown * 2), text);
        Assert.Contains("1000 bytes total", text);
    }

    [Fact]
    public void Decimal_KeepsItsScale_AsString() =>
        Assert.Equal("12.3400", ValueJson.Convert(12.3400m));

    [Fact]
    public void HighPrecisionDecimal_IsPlainDecimalString() =>
        Assert.Equal("123456789012345678.1234567890", ValueJson.Convert(new HighPrecisionDecimal(BigInteger.Parse("1234567890123456781234567890"), 10)));

    [Fact]
    public void DateTime_IsIso8601_WithFullTicks() =>
        Assert.Equal("2026-10-12T09:30:15.1234567", ValueJson.Convert(new DateTime(2026, 10, 12, 9, 30, 15).AddTicks(1234567)));

    [Fact]
    public void NonFiniteDouble_BecomesString_SoJsonStaysValid() =>
        Assert.Equal("NaN", ValueJson.Convert(double.NaN));

    [Fact]
    public void Guid_Bool_Int_PassThrough()
    {
        var g = Guid.NewGuid();
        Assert.Equal(g.ToString(), ValueJson.Convert(g));
        Assert.Equal(true, ValueJson.Convert(true));
        Assert.Equal(7, ValueJson.Convert(7));
    }

    [Fact]
    public void ClrBinary_KeepsTypeAndHex()
    {
        var dict = (Dictionary<string, object?>)ValueJson.Convert(new SqlClrBinaryValue(new byte[] { 1, 2 }, "geography"))!;
        Assert.Equal("geography", dict["type"]);
        Assert.Equal("0x0102", dict["hex"]);
    }

    [Fact]
    public void NullRow_StaysNull_AndNullValuesStayNull()
    {
        Assert.Null(ValueJson.ConvertRow(null));
        var row = ValueJson.ConvertRow(new Dictionary<string, object?> { ["A"] = null })!;
        Assert.True(row.ContainsKey("A"));
        Assert.Null(row["A"]);
    }
}

public class ResultBuilderTests
{
    private static readonly HistoryContext Context = new("dbo.T", "Db", "17.0.1000.7", true, 0);

    private static RowEvent Delete(int seq, object? name = null, DateTime? at = null) =>
        Events.Make(RowEventKind.Delete, seq, at, before: new() { ["Id"] = seq, ["Name"] = name ?? $"row{seq}" });

    [Fact]
    public void KeepsTheMostRecentRows_InAscendingOrder_AndSaysSo()
    {
        var history = Enumerable.Range(1, 10).Select(i => Delete(i)).ToList();

        var result = ResultBuilder.Build(history, Events.Request(maxRows: 3), Context);

        Assert.Equal(new[] { 8, 9, 10 }, result.Events.Select(e => (int)e.Before!["Id"]!).ToArray());
        Assert.True(result.Summary.Truncated);
        Assert.Equal(10, result.Summary.Matched);
        Assert.Equal(3, result.Summary.Returned);
        Assert.Contains(result.Summary.Notes, n => n.Contains("3 most recent of 10"));
    }

    [Fact]
    public void ZeroMaxRows_ReturnsCountsOnly()
    {
        var history = Enumerable.Range(1, 4).Select(i => Delete(i)).ToList();

        var result = ResultBuilder.Build(history, Events.Request(maxRows: 0), Context);

        Assert.Empty(result.Events);
        Assert.Equal(4, result.Summary.Matched);
        Assert.Equal(4, result.Summary.EventsInLogByKind["delete"]);
    }

    [Fact]
    public void EmptyHistory_ExplainsLogRotation()
    {
        var result = ResultBuilder.Build([], Events.Request(), Context);

        Assert.Contains(result.Summary.Notes, n => n.Contains("LogCarverOffline"));
        Assert.Equal(0, result.Summary.EventsInLog);
    }

    [Fact]
    public void NeedsManualReview_IsCountedAndExplained()
    {
        var history = new[]
        {
            Delete(1),
            Events.Make(RowEventKind.Delete, 2, note: "off-row value not decoded"),
        };

        var result = ResultBuilder.Build(history, Events.Request(), Context);

        Assert.Equal(1, result.Summary.NeedsManualReview);
        Assert.Contains(result.Summary.Notes, n => n.Contains("needsManualReview"));
        Assert.Equal("off-row value not decoded", result.Events[1].Note);
    }

    [Fact]
    public void OversizedOutput_IsCut_KeepingTheNewest()
    {
        string big = new('x', 30_000);
        var history = Enumerable.Range(1, 6).Select(i => Delete(i, big)).ToList();

        var result = ResultBuilder.Build(history, Events.Request(maxRows: 500), Context);

        Assert.True(result.Summary.Truncated);
        Assert.True(result.Events.Count < 6);
        Assert.Equal(6, (int)result.Events[^1].Before!["Id"]!);
        Assert.True(ResultBuilder.ToJson(result).Length < ResultBuilder.MaxResponseChars + 5_000);
    }

    [Fact]
    public void UndoSql_OnlyWhenAsked()
    {
        var history = new[] { Delete(1) };

        Assert.Null(ResultBuilder.Build(history, Events.Request(undo: false), Context).Events[0].UndoSql);

        var withUndo = ResultBuilder.Build(history, Events.Request(undo: true), Context).Events[0].UndoSql;
        Assert.NotNull(withUndo);
        Assert.Contains("INSERT", withUndo, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Json_KeepsChineseReadable_AndBinaryAsHex()
    {
        var history = new[]
        {
            Events.Make(RowEventKind.Delete, 1,
                before: new() { ["Name"] = "訂單", ["Blob"] = new byte[] { 0xAB }, ["Amount"] = 1.50m, ["Gone"] = null }),
        };

        string json = ResultBuilder.ToJson(ResultBuilder.Build(history, Events.Request(), Context));

        Assert.Contains("訂單", json);
        Assert.Contains("\"0xAB\"", json);
        Assert.DoesNotContain("System.Byte[]", json);
        using var doc = JsonDocument.Parse(json);
        var before = doc.RootElement.GetProperty("events")[0].GetProperty("before");
        Assert.Equal("1.50", before.GetProperty("Amount").GetString());
        Assert.Equal(JsonValueKind.Null, before.GetProperty("Gone").ValueKind);
    }

    [Fact]
    public void DdlBoundary_NoteNamesTheBoundaries()
    {
        var ctx = Context with { DdlBoundaryCount = 2, DdlBoundaryNames = ["CREATE TABLE", "ALTER TABLE"] };

        var result = ResultBuilder.Build([Delete(1)], Events.Request(), ctx);

        var note = Assert.Single(result.Summary.Notes, n => n.Contains("DDL boundary"));
        Assert.Contains("CREATE TABLE, ALTER TABLE", note);
    }

    [Fact]
    public void UnvalidatedServerVersion_IsFlagged()
    {
        var result = ResultBuilder.Build([Delete(1)], Events.Request(), Context with { VersionValidated = false });
        Assert.Contains(result.Summary.Notes, n => n.Contains("not been validated"));
    }
}
