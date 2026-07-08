namespace FPTUniRAG.Services;

public sealed class FixedChunkingService : IFixedChunkingService
{
    public IReadOnlyList<string> CreateChunks(string content, int chunkSize, int chunkOverlap)
    {
        var normalizedContent = content.Trim();
        if (string.IsNullOrWhiteSpace(normalizedContent))
        {
            return [];
        }

        if (chunkSize <= 0)
        {
            throw new InvalidOperationException("Chunk size must be greater than zero.");
        }

        if (chunkOverlap < 0 || chunkOverlap >= chunkSize)
        {
            throw new InvalidOperationException("Chunk overlap must be zero or greater and smaller than chunk size.");
        }

        var chunks = new List<string>();
        var start = 0;
        while (start < normalizedContent.Length)
        {
            var length = Math.Min(chunkSize, normalizedContent.Length - start);
            var chunk = normalizedContent.Substring(start, length).Trim();
            if (!string.IsNullOrWhiteSpace(chunk))
            {
                chunks.Add(chunk);
            }

            if (start + length >= normalizedContent.Length)
            {
                break;
            }

            start += chunkSize - chunkOverlap;
        }

        return chunks;
    }
}
