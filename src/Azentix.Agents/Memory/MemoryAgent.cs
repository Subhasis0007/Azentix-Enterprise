
using Microsoft.Extensions.Logging;

namespace Azentix.Agents.Memory;

public class MemoryAgent : IMemoryAgent
{
    private readonly IVectorMemory _store;
    private readonly ILogger<MemoryAgent> _log;

    public MemoryAgent(IVectorMemory store, ILogger<MemoryAgent> log)
    {
        _store = store;
        _log = log;
    }

    public Task StoreAsync(string collection, string key, string content,
        string description, float[] embedding, CancellationToken ct = default)
    {
        _log.LogDebug("Store: {Key} -> {Col}", key, collection);

        return _store.SaveAsync(
            collection,
            key,
            content,
            description,
            embedding,
            "{}",
            ct
        );
    }

    public Task<List<MemorySearchResult>> RecallAsync(string collection,
        float[] queryEmbedding, int topK = 5, CancellationToken ct = default)
    {
        _log.LogDebug("Recall: {Col} top{N}", collection, topK);
        return _store.SearchAsync(collection, queryEmbedding, topK, 0.7, ct);
    }

    public Task ClearSessionAsync(string agentId, CancellationToken ct = default)
        => _store.ClearWorkingMemoryAsync(agentId, ct);
}
