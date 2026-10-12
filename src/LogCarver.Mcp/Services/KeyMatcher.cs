using System.Globalization;
using LogCarver.Core;
using LogCarver.Core.SqlServer;

namespace LogCarver.Mcp.Services;

/// <summary>
/// Matches a "Column=Value" key against an event's before or after image.
/// Core's own filter compares value.ToString() under the current culture, so
/// a datetime, bit, decimal or binary key never matches the way an agent
/// would write it (and it is case-sensitive on the column name). This one
/// compares by type, with invariant parsing.
/// </summary>
public static class KeyMatcher
{
    public static bool Matches(RowEvent e, string column, string value) =>
        InImage(e.Before, column, value) || InImage(e.After, column, value);

    private static bool InImage(IReadOnlyDictionary<string, object?>? image, string column, string key)
    {
        if (image is null) return false;
        foreach (var (name, value) in image)
        {
            if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                return ValueEquals(value, key);
        }
        return false;
    }

    public static bool ValueEquals(object? value, string key)
    {
        key = key.Trim();
        switch (value)
        {
            case null:
                return key.Equals("NULL", StringComparison.OrdinalIgnoreCase);
            case string s:
                return string.Equals(s, key, StringComparison.OrdinalIgnoreCase);
            case bool b:
                if (key is "1") return b;
                if (key is "0") return !b;
                return bool.TryParse(key, out var kb) && kb == b;
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                return decimal.TryParse(key, NumberStyles.Number, CultureInfo.InvariantCulture, out var ki)
                       && ki == Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            case decimal d:
                return decimal.TryParse(key, NumberStyles.Number, CultureInfo.InvariantCulture, out var kd) && kd == d;
            case double d:
                return double.TryParse(key, NumberStyles.Float, CultureInfo.InvariantCulture, out var kdb) && kdb == d;
            case float f:
                return float.TryParse(key, NumberStyles.Float, CultureInfo.InvariantCulture, out var kf) && kf == f;
            case HighPrecisionDecimal h:
                return string.Equals(TrimZeros(h.ToString()), TrimZeros(key), StringComparison.Ordinal);
            case DateTime dt:
                return DateTime.TryParse(key, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var kt) && kt == dt;
            case Guid g:
                return Guid.TryParse(key, out var kg) && kg == g;
            case byte[] bytes:
                return HexEquals(bytes, key);
            case SqlClrBinaryValue clr:
                return HexEquals(clr.Bytes, key);
            default:
                return string.Equals(value.ToString(), key, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool HexEquals(byte[] bytes, string key)
    {
        if (key.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            key = key[2..];
        return string.Equals(Convert.ToHexString(bytes), key, StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimZeros(string number)
    {
        if (!number.Contains('.')) return number;
        number = number.TrimEnd('0');
        return number.EndsWith('.') ? number[..^1] : number;
    }
}
