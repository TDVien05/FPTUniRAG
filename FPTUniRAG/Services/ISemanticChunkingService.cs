namespace FPTUniRAG.Services;

public interface ISemanticChunkingService
{
    IReadOnlyList<string> CreateChunks(string content, int maxChunkSize, int minChunkSize);
}
