namespace FPTUniRAG.Services;

public interface IFixedChunkingService
{
    IReadOnlyList<string> CreateChunks(string content, int chunkSize, int chunkOverlap);
}
