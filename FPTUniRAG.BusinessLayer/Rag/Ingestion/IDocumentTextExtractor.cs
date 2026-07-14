namespace FPTUniRAG.BusinessLayer.Rag.Ingestion;

public interface IDocumentTextExtractor
{
    Task<string> ExtractTextAsync(Stream stream, string fileName, CancellationToken cancellationToken = default);
}
