using System.Text.RegularExpressions;

namespace FPTUniRAG.BusinessLayer.Services;

public sealed class SemanticChunkingService : ISemanticChunkingService
{
    private static readonly Regex ParagraphSeparatorRegex = new(@"\r?\n\s*\r?\n", RegexOptions.Compiled);
    private static readonly Regex SentenceSeparatorRegex = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);

    public IReadOnlyList<string> CreateChunks(string content, int maxChunkSize, int minChunkSize)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        if (maxChunkSize <= 0)
        {
            throw new InvalidOperationException("RagIngestion:Semantic:MaxChunkSize must be greater than zero.");
        }

        if (minChunkSize <= 0 || minChunkSize > maxChunkSize)
        {
            throw new InvalidOperationException("RagIngestion:Semantic:MinChunkSize must be greater than zero and smaller than or equal to MaxChunkSize.");
        }

        var normalizedContent = content.Replace("\r\n", "\n").Trim();
        var paragraphs = ParagraphSeparatorRegex
            .Split(normalizedContent)
            .SelectMany(paragraph => SplitOversizedParagraph(paragraph, maxChunkSize))
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();

        if (paragraphs.Count == 0)
        {
            return [];
        }

        var chunks = new List<string>();
        var currentChunk = string.Empty;

        foreach (var paragraph in paragraphs)
        {
            if (string.IsNullOrEmpty(currentChunk))
            {
                currentChunk = paragraph.Trim();
                continue;
            }

            var candidate = $"{currentChunk}\n\n{paragraph.Trim()}";
            if (candidate.Length <= maxChunkSize)
            {
                currentChunk = candidate;
                continue;
            }

            if (currentChunk.Length < minChunkSize)
            {
                currentChunk = candidate;
                continue;
            }

            chunks.Add(currentChunk);
            currentChunk = paragraph.Trim();
        }

        if (!string.IsNullOrWhiteSpace(currentChunk))
        {
            if (chunks.Count > 0 && currentChunk.Length < minChunkSize)
            {
                chunks[^1] = $"{chunks[^1]}\n\n{currentChunk}";
            }
            else
            {
                chunks.Add(currentChunk);
            }
        }

        return chunks;
    }

    private static IEnumerable<string> SplitOversizedParagraph(string paragraph, int maxChunkSize)
    {
        var normalizedParagraph = paragraph.Trim();
        if (string.IsNullOrWhiteSpace(normalizedParagraph))
        {
            yield break;
        }

        var sentences = SentenceSeparatorRegex
            .Split(normalizedParagraph)
            .Select(sentence => sentence.Trim())
            .Where(sentence => !string.IsNullOrWhiteSpace(sentence))
            .ToList();

        if (sentences.Count <= 1)
        {
            foreach (var part in SplitLongText(normalizedParagraph, maxChunkSize))
            {
                yield return part;
            }
            yield break;
        }

        foreach (var sentence in sentences)
        {
            foreach (var part in SplitLongText(sentence, maxChunkSize))
            {
                yield return part;
            }
        }
    }

    private static IEnumerable<string> SplitLongText(string text, int maxChunkSize)
    {
        if (text.Length <= maxChunkSize)
        {
            yield return text;
            yield break;
        }

        var startIndex = 0;
        while (startIndex < text.Length)
        {
            var takeLength = Math.Min(maxChunkSize, text.Length - startIndex);
            yield return text.Substring(startIndex, takeLength).Trim();
            startIndex += takeLength;
        }
    }
}
