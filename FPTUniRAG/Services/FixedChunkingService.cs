namespace FPTUniRAG.Services;

public sealed class FixedChunkingService : IFixedChunkingService
{
    public IReadOnlyList<string> CreateChunks(string content, int chunkSize, int chunkOverlap)
    {
        var normalizedContent = NormalizeLineEndings(content).Trim();
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

        var countedCharacterIndexes = new List<int>(normalizedContent.Length);
        for (var index = 0; index < normalizedContent.Length; index++)
        {
            if (ChunkCharacterCounter.IsCountedCharacter(normalizedContent[index]))
            {
                countedCharacterIndexes.Add(index);
            }
        }

        if (countedCharacterIndexes.Count == 0)
        {
            return [];
        }

        var chunks = new List<string>();
        var countedStart = 0;
        while (countedStart < countedCharacterIndexes.Count)
        {
            var countedEnd = Math.Min(countedStart + chunkSize, countedCharacterIndexes.Count);
            var sourceStart = countedCharacterIndexes[countedStart];
            var sourceEnd = countedEnd < countedCharacterIndexes.Count
                ? countedCharacterIndexes[countedEnd]
                : normalizedContent.Length;

            chunks.Add(normalizedContent.Substring(sourceStart, sourceEnd - sourceStart));

            if (countedEnd >= countedCharacterIndexes.Count)
            {
                break;
            }

            countedStart += chunkSize - chunkOverlap;
        }

        return chunks;
    }

    private static string NormalizeLineEndings(string content)
    {
        return content.Replace("\r\n", "\n").Replace('\r', '\n');
    }
}
