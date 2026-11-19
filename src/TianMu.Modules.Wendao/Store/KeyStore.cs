using Windows.Security.Credentials;

namespace TianMu.Modules.Wendao.Store;

/// <summary>各家金鑰存在 Windows 認證管理員（加密、只有你的帳號讀得到），不寫進任何檔案。</summary>
internal static class KeyStore
{
    private const string Resource = "TianMuIsland.Wendao";
    private static readonly Dictionary<string, string?> Cache = [];

    public static string? Get(string providerId)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(providerId, out var cached)) return cached;
            string? value = null;
            try
            {
                var c = new PasswordVault().Retrieve(Resource, providerId);
                c.RetrievePassword();
                value = c.Password;
            }
            catch (Exception) { /* 沒有存過 */ }
            Cache[providerId] = value;
            return value;
        }
    }

    public static void Set(string providerId, string? key)
    {
        lock (Cache)
        {
            var vault = new PasswordVault();
            try { vault.Remove(vault.Retrieve(Resource, providerId)); } catch (Exception) { }
            key = key?.Trim();
            if (!string.IsNullOrEmpty(key)) vault.Add(new PasswordCredential(Resource, providerId, key));
            Cache[providerId] = string.IsNullOrEmpty(key) ? null : key;
        }
    }
}
