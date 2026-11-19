namespace TianMu.Modules.Wendao.Ai;

/// <summary>
/// 轉接中樞：管理各家連線與模型清單，依手動選擇或自動分派送出問題，失敗就換下一個候選。
/// </summary>
public sealed class Conductor(ChatClient client, Func<string, string?> keyOf)
{
    public const string Auto = "auto";
    private readonly Dictionary<string, List<ModelInfo>> _models = [];

    public IReadOnlyList<ModelInfo> Models => _models.Values.SelectMany(x => x).ToList();

    public bool IsReady(Provider p) => !p.NeedsKey || !string.IsNullOrWhiteSpace(keyOf(p.Id));

    /// <summary>重新抓所有已設定供應商的模型清單；回傳每家的結果（成功的模型數或錯誤訊息）。</summary>
    public async Task<Dictionary<string, string>> RefreshAsync(CancellationToken ct)
    {
        var report = new Dictionary<string, string>();
        var tasks = Provider.All.Where(IsReady).Select(async p =>
        {
            try
            {
                var list = await client.ListModelsAsync(p, keyOf(p.Id), ct).ConfigureAwait(false);
                lock (_models) _models[p.Id] = list;
                lock (report) report[p.Id] = list.Count == 0 ? "沒有可用的免費模型" : $"{list.Count} 個模型";
            }
            catch (ProviderException e)
            {
                lock (_models) _models.Remove(p.Id);
                lock (report) report[p.Id] = e.Message;
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
        foreach (var p in Provider.All.Where(p => !IsReady(p))) { _models.Remove(p.Id); report[p.Id] = "尚未設定金鑰"; }
        return report;
    }

    /// <summary>這次要依序嘗試哪些模型。</summary>
    public List<ModelInfo> Candidates(string choice, TaskKind kind)
    {
        var all = Models;
        if (choice != Auto)
        {
            var picked = all.FirstOrDefault(m => m.Key == choice);
            // 手動選的放第一個；它失敗時再依任務自動找替代
            var rest = Router.Rank(all.Where(m => m.Key != choice), kind);
            return picked is null ? rest.Take(4).ToList() : [picked, .. rest.Take(3)];
        }
        // 自動：同一家最多排兩個，避免一家額度用完時連續失敗
        var ranked = Router.Rank(all, kind);
        var result = new List<ModelInfo>();
        var perProvider = new Dictionary<string, int>();
        foreach (var m in ranked)
        {
            perProvider.TryGetValue(m.ProviderId, out int n);
            if (n >= 2) continue;
            perProvider[m.ProviderId] = n + 1;
            result.Add(m);
            if (result.Count == 5) break;
        }
        return result;
    }

    /// <summary>
    /// 問一題：依序嘗試候選模型，回報正在用哪個（<paramref name="using"/>）、一段段吐字（<paramref name="onText"/>）。
    /// 已經開始回答之後才失敗就不換（避免答案拼接兩家）。
    /// </summary>
    public async Task<ModelInfo> AskAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ModelInfo> candidates,
        Action<ModelInfo, string?> @using, Action<string> onText, CancellationToken ct)
    {
        if (candidates.Count == 0) throw new ProviderException("還沒有可用的模型。請先到「連線」貼上至少一家的免費金鑰。", null, false);
        var errors = new List<string>();
        foreach (var m in candidates)
        {
            var p = Provider.Find(m.ProviderId)!;
            @using(m, errors.Count == 0 ? null : errors[^1]);
            bool started = false;
            try
            {
                await foreach (var piece in client.StreamAsync(p, keyOf(p.Id), m.Id, messages, ct).ConfigureAwait(false))
                {
                    started = true;
                    onText(piece);
                }
                return m;
            }
            catch (ProviderException e) when (!started && e.TryNext)
            {
                errors.Add(e.Message);
            }
        }
        throw new ProviderException("每一個候選模型都失敗了：\n• " + string.Join("\n• ", errors), null, false);
    }
}
