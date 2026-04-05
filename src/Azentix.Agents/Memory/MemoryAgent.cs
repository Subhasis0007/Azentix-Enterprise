using Microsoft.Extensions.Logging;

namespace Azentix.Agents.Memory;

public interface IMemoryAgent
{
    Task StoreAsync(string collection, string key, string content,
        float[] embedding, CancellationToken ct = default);
    Task<List<MemoryResult>> RecallAsync(string collection,
        float[] queryEmbedding, int topK = 5, CancellationToken ct = default);
    Task ClearSessionAsync(string agentId, CancellationToken ct = default);
}

public class MemoryAgent : IMemoryAgent
{
    private readonly IVectorMemory _store;
    private readonly ILogger<MemoryAgent> _log;

    public MemoryAgent(IVectorMemory store, ILogger<MemoryAgent> log)
    { _store = store; _log = log; }

    public Task StoreAsync(string collection, string key, string content,
        float[] embedding, CancellationToken ct = default)
    {
        _log.LogDebug("Store: {Key} -> {Col}", key, collection);
        return _store.SaveAsync(collection, key, content, embedding, "{}", ct);
    }

    public Task<List<MemoryResult>> RecallAsync(string collection,
        float[] queryEmbedding, int topK = 5, CancellationToken ct = default)
    {
        _log.LogDebug("Recall: {Col} top{N}", collection, topK);
        return _store.SearchAsync(collection, queryEmbedding, topK, 0.7, ct);
    }

    public Task ClearSessionAsync(string agentId, CancellationToken ct = default)
        => _store.DeleteWorkingMemoryAsync(agentId, ct);
}
