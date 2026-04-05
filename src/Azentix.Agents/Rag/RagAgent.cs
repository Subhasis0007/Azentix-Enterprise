using Azure.AI.OpenAI;
using OpenAI.Embeddings;
using Microsoft.Extensions.Logging;
using Azentix.Agents.Memory;

namespace Azentix.Agents.Rag;

public interface IRagAgent
{
    Task<string> SearchAsync(string query, string collection = "default", int topK = 5, CancellationToken ct = default);
}

public class RagAgent : IRagAgent
{
    private readonly IVectorMemory _memory;
    private readonly EmbeddingClient _embeddings;
    private readonly ILogger<RagAgent> _logger;

    public RagAgent(IVectorMemory memory, EmbeddingClient embeddings, ILogger<RagAgent> logger)
    { _memory = memory; _embeddings = embeddings; _logger = logger; }

    public async Task<string> SearchAsync(string query, string collection = "default", int topK = 5, CancellationToken ct = default)
    {
        _logger.LogInformation("RagAgent search: '{Q}' in '{Col}'", query[..Math.Min(50, query.Length)], collection);
        var embeddingResult = await _embeddings.GenerateEmbeddingAsync(query, cancellationToken: ct);
        var vector = embeddingResult.Value.ToFloats().ToArray();
        var results = await _memory.SearchAsync(collection, vector, topK, 0.7, ct);
        if (!results.Any()) return "No relevant documents found in knowledge base.";
        var formatted = results.Select((r, i) =>
            $"[{i+1}] Relevance: {r.Relevance:P0} | Source: {r.Source}\n{r.Text}");
        return string.Join("\n\n", formatted);
    }
}
