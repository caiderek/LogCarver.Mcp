using System.Globalization;
using LogCarver.Core;

namespace LogCarver.Mcp.Output;

/// <summary>
/// Turns a decoded column value into something that serializes faithfully
/// to JSON. LogCarver.Core's own exporter stringifies byte[] as the literal
/// text "System.Byte[]", so it can't be used here.
/// </summary>
public static class ValueJson
{
    public const int MaxBinaryBytesShown = 256;

    public static object? Convert(object? value) => value switch
    {
        null => null,
        string or bool or int or long or short or byte or sbyte or ushort or uint or ulong => value,
        double d => double.IsFinite(d) ? d : d.ToString(CultureInfo.InvariantCulture),
        float f => float.IsFinite(f) ? f : f.ToString(CultureInfo.InvariantCulture),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        HighPrecisionDecimal h => h.ToString(),
        DateTime dt => dt.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
        Guid g => g.ToString(),
        byte[] bytes => Hex(bytes),
        SqlClrBinaryValue clr => new Dictionary<string, object?> { ["type"] = clr.TypeName, ["hex"] = Hex(clr.Bytes) },
        _ => value.ToString(),
    };

    public static Dictionary<string, object?>? ConvertRow(IReadOnlyDictionary<string, object?>? row)
    {
        if (row is null) return null;
        var result = new Dictionary<string, object?>(row.Count);
        foreach (var (column, value) in row)
            result[column] = Convert(value);
        return result;
    }

    private static string Hex(byte[] bytes)
    {
        if (bytes.Length == 0) return "0x";
        if (bytes.Length <= MaxBinaryBytesShown)
            return "0x" + System.Convert.ToHexString(bytes);
        return "0x" + System.Convert.ToHexString(bytes, 0, MaxBinaryBytesShown) + $"... ({bytes.Length} bytes total, truncated)";
    }
}
