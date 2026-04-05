-- ============================================================
-- Azentix — Supabase pgvector schema
-- Run this ENTIRE block in Supabase SQL Editor before Month 2
-- ============================================================

CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS agent_memory (
    id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    content     TEXT        NOT NULL,
    summary     TEXT,
    embedding   VECTOR(1536),
    collection  TEXT        NOT NULL,
    agent_id    TEXT,
    task_id     TEXT,
    tenant_id   TEXT        DEFAULT 'default',
    scope       TEXT        DEFAULT 'LongTerm',
    source      TEXT,
    category    TEXT,
    metadata    JSONB       DEFAULT '{}',
    stored_at   TIMESTAMPTZ DEFAULT NOW(),
    expires_at  TIMESTAMPTZ
);

-- HNSW index: fast cosine similarity search
CREATE INDEX IF NOT EXISTS idx_agent_memory_hnsw
    ON agent_memory USING hnsw (embedding vector_cosine_ops)
    WITH (m = 16, ef_construction = 64);

CREATE INDEX IF NOT EXISTS idx_agent_memory_collection ON agent_memory (collection);
CREATE INDEX IF NOT EXISTS idx_agent_memory_tenant     ON agent_memory (tenant_id);
CREATE INDEX IF NOT EXISTS idx_agent_memory_scope      ON agent_memory (scope);

-- Vector search function (called by RagAgent.SearchAsync)
CREATE OR REPLACE FUNCTION match_documents(
    query_embedding   VECTOR(1536),
    match_collection  TEXT,
    match_count       INT   DEFAULT 5,
    min_relevance     FLOAT DEFAULT 0.7
)
RETURNS TABLE (
    id         UUID,
    content    TEXT,
    summary    TEXT,
    source     TEXT,
    metadata   JSONB,
    similarity FLOAT
)
LANGUAGE plpgsql AS $$
BEGIN
    RETURN QUERY
    SELECT
        am.id, am.content, am.summary, am.source, am.metadata,
        1 - (am.embedding <=> query_embedding) AS similarity
    FROM   agent_memory am
    WHERE  am.collection = match_collection
    AND    1 - (am.embedding <=> query_embedding) >= min_relevance
    ORDER  BY am.embedding <=> query_embedding
    LIMIT  match_count;
END;
$$;

-- Verify installation
SELECT 'Supabase schema installed successfully.' AS status;
SELECT COUNT(*) AS total_records FROM agent_memory;
