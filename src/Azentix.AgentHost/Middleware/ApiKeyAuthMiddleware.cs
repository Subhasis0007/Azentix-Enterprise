using System.Security.Cryptography;
using System.Text;

namespace Azentix.AgentHost.Middleware;

/// <summary>
/// App-level API key enforcement — secondary defence behind Kong.
/// Protects /api/** and /mcp/** routes when INTERNAL_API_KEY is set.
/// Paths that are always public: /, /health, /dashboard, /scalar/**, /openapi/**.
/// </summary>
public class ApiKeyAuthMiddleware
{
    private static readonly HashSet<string> _publicPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/",
        "/health",
        "/dashboard",
    };
    private static readonly string[] _publicWildcardPrefixes =
        ["/scalar", "/openapi", "/favicon"];

    private readonly RequestDelegate _next;
    private readonly IConfiguration  _config;
    private readonly ILogger<ApiKeyAuthMiddleware> _log;

    public ApiKeyAuthMiddleware(RequestDelegate next, IConfiguration config,
        ILogger<ApiKeyAuthMiddleware> log)
    {
        _next   = next;
        _config = config;
        _log    = log;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";

        if (IsPublicPath(path))
        {
            await _next(context);
            return;
        }

        var expectedKey = _config["INTERNAL_API_KEY"];
        if (string.IsNullOrWhiteSpace(expectedKey))
        {
            // Key not configured → allow (development / internal-only mode)
            await _next(context);
            return;
        }

        context.Request.Headers.TryGetValue("X-API-Key", out var providedKeyValues);
        var providedKey = providedKeyValues.FirstOrDefault() ?? string.Empty;

        if (!ConstantTimeEquals(providedKey, expectedKey))
        {
            _log.LogWarning("Rejected request to {Path} — missing or invalid X-API-Key", path);
            context.Response.StatusCode  = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                """{"error":"Unauthorized","detail":"Missing or invalid X-API-Key header"}""");
            return;
        }

        await _next(context);
    }

    private static bool IsPublicPath(string path)
    {
        if (_publicPrefixes.Contains(path))
            return true;

        foreach (var prefix in _publicWildcardPrefixes)
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    /// <summary>Constant-time string comparison to prevent timing attacks.</summary>
    private static bool ConstantTimeEquals(string a, string b)
    {
        var bytesA = Encoding.UTF8.GetBytes(a.PadRight(64));
        var bytesB = Encoding.UTF8.GetBytes(b.PadRight(64));
        return CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }
}
