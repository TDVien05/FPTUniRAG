namespace FPTUniRAG.BusinessLayer.Services;

public interface ISemanticChunkingService
{
    IReadOnlyList<string> CreateChunks(string content, int maxChunkSize, int minChunkSize);
}
