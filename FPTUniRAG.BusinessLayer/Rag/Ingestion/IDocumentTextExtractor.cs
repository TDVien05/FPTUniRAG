namespace FPTUniRAG.BusinessLayer.Rag.Ingestion;

public interface IDocumentTextExtractor
{
    /// <param name="onPageExtracted">
    /// Invoked after each page finishes extraction (including its OCR pass), with the number of pages
    /// processed so far and the total page count. Only raised for multi-page formats such as PDF.
    /// </param>
    Task<string> ExtractTextAsync(
        Stream stream,
        string fileName,
        Func<int, int, CancellationToken, Task>? onPageExtracted = null,
        CancellationToken cancellationToken = default);
}
