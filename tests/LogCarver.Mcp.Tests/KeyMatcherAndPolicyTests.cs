using System.Numerics;
using LogCarver.Core;
using LogCarver.Core.SqlServer;
using LogCarver.Mcp.Services;

namespace LogCarver.Mcp.Tests;

public class KeyMatcherTests
{
    [Theory]
    [InlineData(5, "5", true)]
    [InlineData(5, "05", true)]
    [InlineData(5, "6", false)]
    [InlineData(true, "1", true)]
    [InlineData(true, "True", true)]
    [InlineData(false, "0", true)]
    [InlineData(false, "1", false)]
    [InlineData("Alice", "alice", true)]
    [InlineData("Alice", "Bob", false)]
    [InlineData(null, "NULL", true)]
    [InlineData(null, "5", false)]
    public void ScalarValues_MatchTheWayAnAgentWritesThem(object? value, string key, bool expected) =>
        Assert.Equal(expected, KeyMatcher.ValueEquals(value, key));

    [Fact]
    public void Decimal_MatchesRegardlessOfScaleOrCulture() =>
        Assert.True(KeyMatcher.ValueEquals(9.5m, "9.50"));

    [Fact]
    public void HighPrecisionDecimal_IgnoresTrailingZeros() =>
        Assert.True(KeyMatcher.ValueEquals(new HighPrecisionDecimal(BigInteger.Parse("1234567890123456781234567890"), 10), "123456789012345678.123456789"));

    [Fact]
    public void DateTime_MatchesIsoText_AtAnyPrecision()
    {
        var stored = new DateTime(2026, 10, 2, 9, 15, 30, 456);
        Assert.True(KeyMatcher.ValueEquals(stored, "2026-10-02T09:15:30.456"));
        Assert.True(KeyMatcher.ValueEquals(stored, "2026-10-02 09:15:30.4560000"));
        Assert.False(KeyMatcher.ValueEquals(stored, "2026-10-02T09:15:31"));
    }

    [Fact]
    public void Binary_MatchesHex_WithOrWithoutPrefix()
    {
        Assert.True(KeyMatcher.ValueEquals(new byte[] { 0xFF, 0x00 }, "0xFF00"));
        Assert.True(KeyMatcher.ValueEquals(new byte[] { 0xFF, 0x00 }, "ff00"));
        Assert.False(KeyMatcher.ValueEquals(new byte[] { 0xFF, 0x00 }, "0xFF01"));
    }

    [Fact]
    public void Guid_Matches()
    {
        var g = Guid.NewGuid();
        Assert.True(KeyMatcher.ValueEquals(g, g.ToString().ToUpperInvariant()));
    }

    [Fact]
    public void ColumnName_IsCaseInsensitive()
    {
        var e = Events.Make(RowEventKind.Delete, 1, before: new() { ["Id"] = 5 });
        Assert.True(KeyMatcher.Matches(e, "id", "5"));
        Assert.False(KeyMatcher.Matches(e, "Missing", "5"));
    }
}

public class DatabasePolicyTests
{
    private static readonly IReadOnlySet<string> NoneAllowed = DatabasePolicy.ParseAllowList(null);

    [Fact]
    public void NoOverride_IsAlwaysFine() => DatabasePolicy.Check("Sales", NoneAllowed, null);

    [Fact]
    public void RequestingTheConfiguredDatabase_IsFine_CaseInsensitively() =>
        DatabasePolicy.Check("Sales", NoneAllowed, "sales");

    [Fact]
    public void AnotherDatabase_IsRefused_ByDefault()
    {
        var ex = Assert.Throws<ToolInputException>(() => DatabasePolicy.Check("Sales", NoneAllowed, "HR"));
        Assert.Contains("LOGCARVER_MCP_ALLOWED_DATABASES", ex.Message);
    }

    [Fact]
    public void AllowListed_IsPermitted_AndParsingAcceptsCommasSemicolonsAndSpaces()
    {
        var allowed = DatabasePolicy.ParseAllowList("HR; Finance , Audit");
        DatabasePolicy.Check("Sales", allowed, "finance");
        Assert.Throws<ToolInputException>(() => DatabasePolicy.Check("Sales", allowed, "msdb"));
    }

    [Fact]
    public void WithNoConfiguredDatabase_AnyOverrideNeedsTheAllowList() =>
        Assert.Throws<ToolInputException>(() => DatabasePolicy.Check("", NoneAllowed, "HR"));
}

public class UndoSqlEscapingTests
{
    [Fact]
    public void HostileStringValue_IsQuoted_NotInterpreted()
    {
        var evt = Events.Make(RowEventKind.Delete, 1,
            before: new() { ["Id"] = 1, ["Name"] = "x'); DROP TABLE dbo.T;--" });

        var result = ResultBuilder.Build([evt], Events.Request(undo: true), new HistoryContext("dbo.T", "Db", "17.0", true, 0));

        string undo = result.Events[0].UndoSql!;
        Assert.Contains("x''); DROP TABLE dbo.T;--", undo);
        Assert.DoesNotContain("'x'); DROP", undo);
    }

    [Fact]
    public void DeleteOnAnIdentityTable_PutsTheRowBackWithItsOriginalIdentityValue()
    {
        var evt = Events.Make(RowEventKind.Delete, 1, before: new() { ["Id"] = 7, ["Name"] = "x" });

        var result = ResultBuilder.Build([evt], Events.Request(undo: true),
            new HistoryContext("dbo.T", "Db", "17.0", true, 0, IdentityColumn: "Id"));

        string undo = result.Events[0].UndoSql!;
        Assert.Contains("SET IDENTITY_INSERT [dbo].[T] ON", undo);
        Assert.Contains("SET IDENTITY_INSERT [dbo].[T] OFF", undo);
    }

    [Fact]
    public void DeleteOnATableWithoutIdentity_IsAPlainInsert()
    {
        var evt = Events.Make(RowEventKind.Delete, 1, before: new() { ["Id"] = 7, ["Name"] = "x" });

        var result = ResultBuilder.Build([evt], Events.Request(undo: true), new HistoryContext("dbo.T", "Db", "17.0", true, 0));

        Assert.DoesNotContain("IDENTITY_INSERT", result.Events[0].UndoSql!);
    }
}
