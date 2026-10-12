using LogCarver.Mcp.Services;

namespace LogCarver.Mcp.Tests;

public class FindRequestParserTests
{
    private static FindRequest Parse(
        string? table = "dbo.Orders", string? database = null, string? operations = null, string? from = null,
        string? to = null, string? key = null, int maxRows = FindRequestParser.DefaultMaxRows, bool includeUndoSql = false) =>
        FindRequestParser.Parse(table, database, operations, from, to, key, maxRows, includeUndoSql);

    [Fact]
    public void Defaults_AreDeleteOnly_Twenty_Rows_NoUndoSql()
    {
        var r = Parse();
        Assert.Equal(OperationFilter.Delete, r.Operations);
        Assert.Equal(20, r.MaxRows);
        Assert.False(r.IncludeUndoSql);
        Assert.Empty(r.Warnings);
    }

    [Theory]
    [InlineData("dbo.Orders")]
    [InlineData("[dbo].[Order Items]")]
    [InlineData("sales.訂單")]
    [InlineData("  dbo.Orders  ")]
    public void ValidTableNames_Accepted(string table) => Assert.NotNull(Parse(table: table));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Orders")]
    [InlineData("dbo.Orders; DROP TABLE x")]
    [InlineData("dbo.Orders--")]
    [InlineData("a.b.c")]
    [InlineData("dbo.[bad]name]")]
    [InlineData("dbo.Or'ders")]
    public void InvalidTableNames_Rejected(string table) =>
        Assert.Throws<ToolInputException>(() => Parse(table: table));

    [Theory]
    [InlineData("delete", OperationFilter.Delete)]
    [InlineData("DELETE", OperationFilter.Delete)]
    [InlineData("update", OperationFilter.Update)]
    [InlineData("insert", OperationFilter.Insert)]
    [InlineData("all", OperationFilter.All)]
    [InlineData("", OperationFilter.Delete)]
    public void Operations_Parsed(string value, OperationFilter expected) =>
        Assert.Equal(expected, Parse(operations: value).Operations);

    [Fact]
    public void UnknownOperation_Rejected() =>
        Assert.Throws<ToolInputException>(() => Parse(operations: "truncate"));

    [Fact]
    public void FromAfterTo_Rejected() =>
        Assert.Throws<ToolInputException>(() => Parse(from: "2026-10-12T10:00", to: "2026-10-12T09:00"));

    [Fact]
    public void UnparseableTime_Rejected() =>
        Assert.Throws<ToolInputException>(() => Parse(from: "yesterday-ish"));

    [Fact]
    public void Time_IsKeptAsWritten_NoZoneConversion()
    {
        var r = Parse(from: "2026-10-12T09:30:00");
        Assert.Equal(new DateTime(2026, 10, 12, 9, 30, 0), r.From);
    }

    [Theory]
    [InlineData("Id=5", "Id", "5")]
    [InlineData(" Name = a=b ", "Name", "a=b")]
    [InlineData("Note=", "Note", "")]
    public void Key_Parsed(string key, string column, string value)
    {
        var r = Parse(key: key);
        Assert.Equal(column, r.KeyColumn);
        Assert.Equal(value, r.KeyValue);
    }

    [Theory]
    [InlineData("Id")]
    [InlineData("=5")]
    public void BadKey_Rejected(string key) => Assert.Throws<ToolInputException>(() => Parse(key: key));

    [Fact]
    public void NegativeMaxRows_Rejected() => Assert.Throws<ToolInputException>(() => Parse(maxRows: -1));

    [Fact]
    public void ZeroMaxRows_Allowed_MeansCountsOnly() => Assert.Equal(0, Parse(maxRows: 0).MaxRows);

    [Fact]
    public void MaxRowsAboveLimit_IsCapped_WithWarning()
    {
        var r = Parse(maxRows: 5000);
        Assert.Equal(FindRequestParser.MaxRowsLimit, r.MaxRows);
        Assert.Single(r.Warnings);
    }

    [Fact]
    public void BlankDatabase_BecomesNull() => Assert.Null(Parse(database: "  ").Database);
}
