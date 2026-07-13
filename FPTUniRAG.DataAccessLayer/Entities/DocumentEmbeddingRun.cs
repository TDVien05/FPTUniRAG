using System;

namespace FPTUniRAG.DataAccessLayer.Entities;

public sealed class DocumentEmbeddingRun
{
    public Guid EmbeddingRunId { get; set; }

    public Guid DocumentId { get; set; }

    public string EmbeddingModel { get; set; } = null!;

    public int EmbeddingDimensions { get; set; }

    public long? DocumentSizeBytes { get; set; }

    public int ChunkCount { get; set; }

    public int VectorCount { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string Status { get; set; } = null!;

    public string? ErrorMessage { get; set; }

    public Document Document { get; set; } = null!;
}
