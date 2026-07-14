namespace FPTUniRAG.BusinessLayer.Rag.Chunking;

public interface ISemanticChunkingService
{
    IReadOnlyList<string> CreateChunks(string content, int maxChunkSize, int minChunkSize);
}
