using Npgsql;
using Pgvector;
using Pgvector.Npgsql;
using Microsoft.Extensions.Logging;
using Azentix.Models;

namespace Azentix.Agents.Memory;

public interface IVectorMemory
{
    Task SaveAsync(string collection, string id, string content,
        float[] embedding, string metadata = "{}", CancellationToken ct = default);
    Task<List<MemoryResult>> SearchAsync(string collection, float[] queryEmbedding,
        int topK = 5, double minRelevance = 0.7, CancellationToken ct = default);
    Task DeleteWorkingMemoryAsync(string agentId, CancellationToken ct = default);
}

public record MemoryResult(string Id, string Text, string Source, double Relevance);

public class SupabaseVectorMemory : IVectorMemory
{
    private readonly string _connectionString;
    private readonly ILogger<SupabaseVectorMemory> _logger;

    public SupabaseVectorMemory(SupabaseConfig cfg, ILogger<SupabaseVectorMemory> logger)
    { _connectionString = cfg.DatabaseConnectionString; _logger = logger; }

    public async Task SaveAsync(string collection, string id, string content,
        float[] embedding, string metadata = "{}", CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO agent_memory (id,content,embedding,collection,metadata,stored_at)
            VALUES (@id,@c,@e,@col,@m::jsonb,NOW())
            ON CONFLICT(id) DO UPDATE
              SET content=EXCLUDED.content, embedding=EXCLUDED.embedding, stored_at=NOW()", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("c",  content);
        cmd.Parameters.AddWithValue("e",  new Vector(embedding));
        cmd.Parameters.AddWithValue("col",collection);
        cmd.Parameters.AddWithValue("m",  metadata);
        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogDebug("Saved {Id} to collection {Col}", id, collection);
    }

    public async Task<List<MemoryResult>> SearchAsync(string collection,
        float[] queryEmbedding, int topK = 5, double minRelevance = 0.7,
        CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
            SELECT id, content, COALESCE(source,''), 1-(embedding<=>@q) AS sim
            FROM   agent_memory
            WHERE  collection = @col
            AND    1-(embedding<=>@q) >= @min
            ORDER  BY embedding <=> @q
            LIMIT  @k", conn);
        cmd.Parameters.AddWithValue("q",   new Vector(queryEmbedding));
        cmd.Parameters.AddWithValue("col", collection);
        cmd.Parameters.AddWithValue("min", minRelevance);
        cmd.Parameters.AddWithValue("k",   topK);
        var results = new List<MemoryResult>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(new MemoryResult(
                reader.GetString(0), reader.GetString(1),
                reader.GetString(2), reader.GetDouble(3)));
        _logger.LogDebug("Search in {Col}: {N} results", collection, results.Count);
        return results;
    }

    public async Task DeleteWorkingMemoryAsync(string agentId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM agent_memory WHERE agent_id=@id AND scope='Working'", conn);
        cmd.Parameters.AddWithValue("id", agentId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new NpgsqlConnection(_connectionString);
        conn.TypeMapper.UseVector();
        await conn.OpenAsync(ct);
        return conn;
    }
}
