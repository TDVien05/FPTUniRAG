namespace FPTUniRAG.BusinessLayer.Services;

public interface ITesseractOcrService
{
    Task<string> ExtractTextAsync(
        IReadOnlyList<byte> imageBytes,
        string fileExtension,
        CancellationToken cancellationToken = default);
}
