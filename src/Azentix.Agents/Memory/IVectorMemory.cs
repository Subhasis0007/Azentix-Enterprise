namespace Azentix.Agents.Memory;

public interface IVectorMemory
{
    Task SaveAsync(string collection, string id, string text, string description,
        float[] embedding, string metadata = "{}", CancellationToken ct = default);
    Task<List<MemorySearchResult>> SearchAsync(string collection, float[] queryEmbedding,
        int topK = 5, double minRelevance = 0.7, CancellationToken ct = default);
    Task ClearWorkingMemoryAsync(string agentId, CancellationToken ct = default);
}

public record MemorySearchResult
{
    public string Id { get; init; } = "";
    public string Text { get; init; } = "";
    public string Description { get; init; } = "";
    public string Source { get; init; } = "";
    public double Relevance { get; set; }
}
