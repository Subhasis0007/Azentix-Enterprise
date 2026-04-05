using Microsoft.Extensions.Logging;

namespace Azentix.Agents.Memory;

public interface IMemoryAgent
{
    Task StoreAsync(string collection, string key, string content, float[] embedding, CancellationToken ct = default);
    Task<List<MemorySearchResult>> RecallAsync(string collection, float[] queryEmbedding, int topK = 5, CancellationToken ct = default);
}

public class MemoryAgent : IMemoryAgent
{
    private readonly IVectorMemory _memory;
    private readonly ILogger<MemoryAgent> _logger;

    public MemoryAgent(IVectorMemory memory, ILogger<MemoryAgent> logger)
    { _memory = memory; _logger = logger; }

    public Task StoreAsync(string collection, string key, string content, float[] embedding, CancellationToken ct = default)
    {
        _logger.LogInformation("MemoryAgent Store: {Key} -> {Col}", key, collection);
        return _memory.SaveAsync(collection, key, content, "", embedding, "{}", ct);
    }

    public Task<List<MemorySearchResult>> RecallAsync(string collection, float[] queryEmbedding, int topK = 5, CancellationToken ct = default)
    {
        _logger.LogInformation("MemoryAgent Recall: {Col} top{K}", collection, topK);
        return _memory.SearchAsync(collection, queryEmbedding, topK, 0.7, ct);
    }
}
