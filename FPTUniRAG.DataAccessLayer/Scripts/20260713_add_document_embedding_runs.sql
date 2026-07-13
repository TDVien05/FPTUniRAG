CREATE TABLE IF NOT EXISTS document_embedding_runs (
    embedding_run_id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    document_id uuid NOT NULL,
    embedding_model character varying(255) NOT NULL,
    embedding_dimensions integer NOT NULL DEFAULT 0,
    document_size_bytes bigint,
    chunk_count integer NOT NULL DEFAULT 0,
    vector_count integer NOT NULL DEFAULT 0,
    started_at timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    completed_at timestamp without time zone,
    status character varying(50) NOT NULL,
    error_message text,
    CONSTRAINT fk_document_embedding_runs_document
        FOREIGN KEY (document_id)
        REFERENCES documents(document_id)
        ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_document_embedding_runs_document_id
    ON document_embedding_runs(document_id);

CREATE INDEX IF NOT EXISTS ix_document_embedding_runs_model
    ON document_embedding_runs(embedding_model);

DO $$
BEGIN
    IF to_regclass('chunk_embeddings') IS NOT NULL THEN
        INSERT INTO document_embedding_runs (
            document_id, embedding_model, embedding_dimensions, chunk_count, vector_count,
            started_at, completed_at, status)
        SELECT c.document_id, ce.embedding_model, ce.embedding_dimensions,
               COUNT(*)::integer, COUNT(*)::integer,
               COALESCE(d.created_at, CURRENT_TIMESTAMP), COALESCE(d.created_at, CURRENT_TIMESTAMP), 'completed'
        FROM chunk_embeddings ce
        JOIN chunks c ON c.chunk_id = ce.chunk_id
        JOIN documents d ON d.document_id = c.document_id
        GROUP BY c.document_id, ce.embedding_model, ce.embedding_dimensions, d.created_at
        ON CONFLICT DO NOTHING;
    END IF;
END $$;
