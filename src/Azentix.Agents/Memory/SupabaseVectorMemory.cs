
using Npgsql;
using Pgvector;
using Pgvector.Npgsql;
using Microsoft.Extensions.Logging;
using Azentix.Models;

namespace Azentix.Agents.Memory;

public class SupabaseVectorMemory : IVectorMemory
{
    private readonly string _connectionString;
    private readonly ILogger<SupabaseVectorMemory> _logger;
    private readonly NpgsqlDataSource _dataSource;

    public SupabaseVectorMemory(SupabaseConfig cfg, ILogger<SupabaseVectorMemory> logger)
    {
        _connectionString = cfg.DatabaseConnectionString;
        _logger = logger;

        // ✅ Npgsql 8+ compliant datasource configuration
        var builder = new NpgsqlDataSourceBuilder(_connectionString);
        builder.UseVector();
        _dataSource = builder.Build();
    }

    public async Task SaveAsync(
        string collection,
        string id,
        string text,
        string description,
        float[] embedding,
        string metadata = "{}",
        CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO agent_memory (id,content,description,embedding,collection,metadata,stored_at)
            VALUES (@id,@c,@d,@e,@col,@m::jsonb,NOW())
            ON CONFLICT(id) DO UPDATE
              SET content=EXCLUDED.content,
                  description=EXCLUDED.description,
                  embedding=EXCLUDED.embedding,
                  stored_at=NOW()", conn);

        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("c", text);
        cmd.Parameters.AddWithValue("d", description);
        cmd.Parameters.AddWithValue("e", new Vector(embedding));
        cmd.Parameters.AddWithValue("col", collection);
        cmd.Parameters.AddWithValue("m", metadata);

        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogDebug("Saved {Id} to collection {Col}", id, collection);
    }

    public async Task<List<MemorySearchResult>> SearchAsync(
        string collection,
        float[] queryEmbedding,
        int topK = 5,
        double minRelevance = 0.7,
        CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
            SELECT id, content, description, COALESCE(source,''), 1-(embedding<=>@q) AS sim
            FROM   agent_memory
            WHERE  collection = @col
            AND    1-(embedding<=>@q) >= @min
            ORDER  BY embedding <=> @q
            LIMIT  @k", conn);

        cmd.Parameters.AddWithValue("q", new Vector(queryEmbedding));
        cmd.Parameters.AddWithValue("col", collection);
        cmd.Parameters.AddWithValue("min", minRelevance);
        cmd.Parameters.AddWithValue("k", topK);

        var results = new List<MemorySearchResult>();

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new MemorySearchResult
            {
                Id = reader.GetString(0),
                Text = reader.GetString(1),
                Description = reader.GetString(2),
                Source = reader.GetString(3),
                Relevance = reader.GetDouble(4)
            });
        }

        _logger.LogDebug("Search in {Col}: {N} results", collection, results.Count);
        return results;
    }

    public async Task ClearWorkingMemoryAsync(string agentId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM agent_memory WHERE agent_id=@id AND scope='Working'",
            conn);

        cmd.Parameters.AddWithValue("id", agentId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        return await _dataSource.OpenConnectionAsync(ct);
    }
}
