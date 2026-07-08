namespace FPTUniRAG.Services;

public interface IDocumentTextExtractor
{
    Task<string> ExtractTextAsync(Stream stream, string fileName, CancellationToken cancellationToken = default);
}
