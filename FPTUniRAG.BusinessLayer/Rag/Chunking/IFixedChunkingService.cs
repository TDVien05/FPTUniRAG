namespace FPTUniRAG.BusinessLayer.Rag.Chunking;

public interface IFixedChunkingService
{
    IReadOnlyList<string> CreateChunks(string content, int chunkSize, int chunkOverlap);
}
