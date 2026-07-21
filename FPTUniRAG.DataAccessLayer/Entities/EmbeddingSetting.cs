namespace FPTUniRAG.DataAccessLayer.Entities;

public sealed class EmbeddingSetting
{
    public short SettingId { get; set; } = 1;

    public string EmbeddingModel { get; set; } = null!;

    public int EmbeddingDimensions { get; set; }

    public int FixedChunkSize { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
