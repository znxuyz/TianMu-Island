using System.IO;
using System.Text.Json;

namespace TianMu.Modules.Ai.Claude;

/// <summary>從 Claude Code 的登入檔讀到的資訊。</summary>
/// <param name="AccessToken">存取權杖（只用來向 Anthropic 查詢額度，不會存檔或送到別處）。</param>
/// <param name="ExpiresAt">權杖到期時間；未知時為 null。</param>
/// <param name="Plan">訂閱方案，例如 "max"、"pro"。</param>
public sealed record ClaudeCodeLogin(string AccessToken, DateTimeOffset? ExpiresAt, string? Plan)
{
    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } at && at <= now.AddMinutes(1);
}

/// <summary>
/// 讀取 Claude Code（命令列或桌面版內的 Code）的登入檔 %USERPROFILE%\.claude\.credentials.json。
///
/// 安全原則：
///   · 只讀不寫，也絕不自行「更新權杖」——那會讓 Claude Code 原本的登入失效
///   · 權杖過期時請使用者開一次 Claude Code，由它自己更新
/// </summary>
public static class ClaudeCodeCredentials
{
    /// <summary>登入檔位置（尊重 CLAUDE_CONFIG_DIR 環境變數）。</summary>
    public static string DefaultPath
    {
        get
        {
            string? configDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            string dir = string.IsNullOrWhiteSpace(configDir)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")
                : configDir;
            return Path.Combine(dir, ".credentials.json");
        }
    }

    public static bool Exists(string? path = null) => File.Exists(path ?? DefaultPath);

    public static ClaudeCodeLogin? TryRead(string? path = null)
    {
        try
        {
            string file = path ?? DefaultPath;
            return File.Exists(file) ? Parse(File.ReadAllText(file)) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>解析登入檔內容；格式不符回傳 null。</summary>
    public static ClaudeCodeLogin? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth)) return null;
            if (!oauth.TryGetProperty("accessToken", out var token) || token.ValueKind != JsonValueKind.String) return null;

            string? value = token.GetString();
            if (string.IsNullOrWhiteSpace(value)) return null;

            DateTimeOffset? expires = null;
            if (oauth.TryGetProperty("expiresAt", out var exp) && exp.ValueKind == JsonValueKind.Number && exp.TryGetInt64(out long ms))
                expires = DateTimeOffset.FromUnixTimeMilliseconds(ms);

            string? plan = oauth.TryGetProperty("subscriptionType", out var sub) && sub.ValueKind == JsonValueKind.String
                ? sub.GetString()
                : null;

            return new ClaudeCodeLogin(value, expires, plan);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
