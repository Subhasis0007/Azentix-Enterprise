using Npgsql;
using Pgvector;
using Pgvector.Npgsql;
using Microsoft.Extensions.Logging;
using Azentix.Models;

namespace Azentix.Agents.Memory;

public class SupabaseVectorMemory : IVectorMemory
{
    private readonly string _conn;
    private readonly ILogger<SupabaseVectorMemory> _logger;

    public SupabaseVectorMemory(SupabaseConfig config, ILogger<SupabaseVectorMemory> logger)
    { _conn = config.DatabaseConnectionString; _logger = logger; }

    public async Task SaveAsync(string collection, string id, string text,
        string description, float[] embedding, string metadata = "{}",
        CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO agent_memory (id,content,summary,embedding,collection,metadata,stored_at) " +
            "VALUES (@id,@content,@summary,@embedding,@collection,@metadata::jsonb,NOW()) " +
            "ON CONFLICT(id) DO UPDATE SET content=EXCLUDED.content,embedding=EXCLUDED.embedding,stored_at=NOW()", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("content", text);
        cmd.Parameters.AddWithValue("summary", description);
        cmd.Parameters.AddWithValue("embedding", new Vector(embedding));
        cmd.Parameters.AddWithValue("collection", collection);
        cmd.Parameters.AddWithValue("metadata", metadata);
        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogDebug("Saved to Supabase: {Id} -> {Col}", id, collection);
    }

    public async Task<List<MemorySearchResult>> SearchAsync(
        string collection, float[] queryEmbedding,
        int topK = 5, double minRelevance = 0.7, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT id,content,summary,source, 1-(embedding<=>@q) AS sim " +
            "FROM agent_memory WHERE collection=@col " +
            "AND 1-(embedding<=>@q)>=@min ORDER BY embedding<=>@q LIMIT @k", conn);
        cmd.Parameters.AddWithValue("q", new Vector(queryEmbedding));
        cmd.Parameters.AddWithValue("col", collection);
        cmd.Parameters.AddWithValue("min", minRelevance);
        cmd.Parameters.AddWithValue("k", topK);
        var results = new List<MemorySearchResult>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            results.Add(new MemorySearchResult {
                Id = r.GetString(0), Text = r.GetString(1),
                Description = r.IsDBNull(2) ? "" : r.GetString(2),
                Source = r.IsDBNull(3) ? "" : r.GetString(3),
                Relevance = r.GetDouble(4) });
        _logger.LogDebug("Supabase search: {N} results in {Col}", results.Count, collection);
        return results;
    }

    public async Task ClearWorkingMemoryAsync(string agentId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM agent_memory WHERE agent_id=@id AND scope='Working'", conn);
        cmd.Parameters.AddWithValue("id", agentId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var c = new NpgsqlConnection(_conn);
        c.TypeMapper.UseVector();
        await c.OpenAsync(ct);
        return c;
    }
}
