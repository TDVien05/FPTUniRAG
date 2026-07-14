namespace FPTUniRAG.BusinessLayer.Rag.Ingestion;

public interface ITesseractOcrService
{
    Task<string> ExtractTextAsync(
        IReadOnlyList<byte> imageBytes,
        string fileExtension,
        CancellationToken cancellationToken = default);
}
