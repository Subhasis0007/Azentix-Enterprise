
namespace Azentix.Agents.Memory;

public interface IMemoryAgent
{
    Task StoreAsync(string collection, string key, string content,
        string description, float[] embedding, CancellationToken ct = default);

    Task<List<MemorySearchResult>> RecallAsync(string collection,
        float[] queryEmbedding, int topK = 5, CancellationToken ct = default);

    Task ClearSessionAsync(string agentId, CancellationToken ct = default);
}
