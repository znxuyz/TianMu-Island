using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TianMu.Modules.Ai.Claude;

/// <summary>
/// 把機密字串（claude.ai 的 sessionKey）以 Windows DPAPI 加密後存檔。
/// 加密綁定目前的 Windows 使用者帳號：檔案被複製到別台電腦或別的帳號也解不開。
/// </summary>
internal sealed class SecretStore(string path)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TianMu-Island/claude-session");

    public bool HasValue => File.Exists(path);

    public string? Read()
    {
        try
        {
            if (!File.Exists(path)) return null;
            byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            return null; // 換了使用者或檔案損毀
        }
    }

    public void Write(string secret)
    {
        byte[] cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, cipher);
    }

    public void Clear()
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
