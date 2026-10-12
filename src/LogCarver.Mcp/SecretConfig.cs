using Microsoft.Extensions.Configuration;

namespace LogCarver.Mcp;

public static class SecretConfig
{
    /// <returns>The value read from the file named by "{name}_FILE" when that variable is set, otherwise from "{name}" directly.</returns>
    public static string? Read(IConfiguration configuration, string name)
    {
        string? filePath = configuration[$"{name}_FILE"];
        if (!string.IsNullOrEmpty(filePath))
            return File.ReadAllText(filePath).Trim();

        return configuration[name];
    }
}
