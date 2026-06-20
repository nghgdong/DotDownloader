using System.Security.Cryptography;
using System.Text.Json;

namespace DM.Server.Security;

/// <summary>
/// Cung cấp shared-secret token cho local server. Ưu tiên biến môi trường (dev),
/// sau đó đọc <c>config.json</c>, cuối cùng tự sinh &amp; lưu lại.
/// </summary>
public sealed class TokenProvider
{
    /// <summary>Biến môi trường ghi đè token (dev: đặt "dev-token" để curl dễ test).</summary>
    public const string DevEnvVar = "DM_DEV_TOKEN";

    private readonly string _configPath;

    public TokenProvider(string? configPath = null)
        => _configPath = configPath ?? DefaultConfigPath();

    public static string DefaultConfigPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DotDownloader");
        return Path.Combine(dir, "config.json");
    }

    public string GetOrCreateToken()
    {
        var fromEnv = Environment.GetEnvironmentVariable(DevEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        var existing = TryReadConfig()?.Token;
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        var token = GenerateToken();
        Save(token);
        return token;
    }

    private ServerConfig? TryReadConfig()
    {
        if (!File.Exists(_configPath))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<ServerConfig>(File.ReadAllText(_configPath));
        }
        catch
        {
            return null;
        }
    }

    private void Save(string token)
    {
        var dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        var cfg = TryReadConfig() ?? new ServerConfig();
        cfg.Token = token;
        File.WriteAllText(_configPath, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private sealed class ServerConfig
    {
        public string? Token { get; set; }
    }
}
