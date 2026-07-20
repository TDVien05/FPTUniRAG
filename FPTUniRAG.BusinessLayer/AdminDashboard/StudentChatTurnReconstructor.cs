using System.Text.Json;
using FPTUniRAG.BusinessLayer.Rag.Chat;
using FPTUniRAG.DataAccessLayer.Repositories.Reporting;

namespace FPTUniRAG.BusinessLayer.AdminDashboard;

public static class StudentChatTurnReconstructor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static StudentChatSessionAnalysisDto BuildSession(StudentChatReportSessionRecord session)
    {
        var usageByMessage = session.Usage
            .Where(usage => usage.MessageId.HasValue)
            .GroupBy(usage => usage.MessageId!.Value)
            .ToDictionary(group => group.Key, Combine);

        var turns = new List<MutableTurn>();
        var pendingPromptIndexes = new List<int>();

        foreach (var message in session.Messages.OrderBy(message => message.CreatedAt).ThenBy(message => message.MessageId))
        {
            if (message.SenderRole.Equals("student", StringComparison.OrdinalIgnoreCase))
            {
                turns.Add(new MutableTurn { Prompt = message });
                pendingPromptIndexes.Add(turns.Count - 1);
                continue;
            }

            if (!message.SenderRole.Equals("assistant", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            MutableTurn turn;
            if (pendingPromptIndexes.Count > 0)
            {
                var pendingIndex = pendingPromptIndexes[^1];
                pendingPromptIndexes.RemoveAt(pendingPromptIndexes.Count - 1);
                turn = turns[pendingIndex];
            }
            else
            {
                turn = new MutableTurn();
                turns.Add(turn);
            }

            turn.Answer = message;
            turn.Usage = usageByMessage.GetValueOrDefault(message.MessageId);
        }

        var mappedTurns = turns.Select((turn, index) => MapTurn(turn, index + 1)).ToArray();
        var answered = mappedTurns.Where(turn => turn.PromptMessageId.HasValue && turn.AnswerMessageId.HasValue).ToArray();
        var usage = session.Usage.ToArray();
        var timestamps = session.Messages.Where(message => message.CreatedAt.HasValue).Select(message => message.CreatedAt!.Value).ToArray();
        var startedAt = timestamps.Length == 0 ? session.StartedAt : timestamps.Min();
        var lastActivityAt = timestamps.Length == 0 ? session.EndedAt ?? session.StartedAt : timestamps.Max();
        // Average over turns, not over raw usage rows, so retried answers count once.
        var durations = mappedTurns.Where(turn => turn.ResponseTimeMs.HasValue).Select(turn => (double)turn.ResponseTimeMs!.Value).ToArray();

        return new StudentChatSessionAnalysisDto(
            session.SessionId,
            session.StudentName,
            session.StudentEmail,
            session.StudentCode,
            session.SubjectCode,
            session.SubjectName,
            startedAt,
            lastActivityAt,
            mappedTurns.Count(turn => turn.PromptMessageId.HasValue),
            answered.Length,
            usage.Sum(item => item.PromptTokens),
            usage.Sum(item => item.CompletionTokens),
            durations.Length == 0 ? null : durations.Average(),
            startedAt.HasValue && lastActivityAt.HasValue
                ? (long)Math.Max(0, Math.Round((lastActivityAt.Value - startedAt.Value).TotalMilliseconds))
                : null,
            mappedTurns);
    }

    // A single answer can produce several usage rows (retries, follow-up provider calls).
    // Merge them so per-turn metrics add up to the session totals shown in the summary cards.
    private static StudentChatReportUsageRecord Combine(IEnumerable<StudentChatReportUsageRecord> usage)
    {
        var ordered = usage.OrderBy(item => item.UsedAt).ThenBy(item => item.TokenUsageId).ToArray();
        var latest = ordered[^1];

        return latest with
        {
            PromptTokens = ordered.Sum(item => item.PromptTokens),
            CompletionTokens = ordered.Sum(item => item.CompletionTokens),
            TotalTokens = ordered.Sum(item => item.TotalTokens),
            RequestCount = ordered.Sum(item => item.RequestCount),
            ResponseTimeMs = ordered.Any(item => item.ResponseTimeMs.HasValue)
                ? ordered.Sum(item => item.ResponseTimeMs ?? 0)
                : null
        };
    }

    private static StudentChatTurnReportDto MapTurn(MutableTurn turn, int number)
    {
        var citations = ParseCitations(turn.Answer?.CitationsJson);
        var retrievalCount = ParseRetrievalCount(turn.Usage?.MetadataJson) ?? (citations.Count == 0 ? null : citations.Count);
        var status = turn.Prompt is null
            ? "unpaired-response"
            : turn.Answer is null
                ? "no-saved-response"
                : turn.Usage is null
                    ? "usage-unavailable"
                    : "answered";

        return new StudentChatTurnReportDto(
            number,
            turn.Prompt?.MessageId,
            turn.Answer?.MessageId,
            turn.Prompt?.MessageContent,
            turn.Answer?.MessageContent,
            turn.Prompt?.CreatedAt,
            turn.Answer?.CreatedAt,
            status,
            turn.Usage?.ProviderName,
            turn.Usage?.ModelName,
            turn.Usage?.PromptTokens,
            turn.Usage?.CompletionTokens,
            turn.Usage?.TotalTokens,
            turn.Usage?.RequestCount,
            turn.Usage?.ResponseTimeMs,
            retrievalCount,
            citations);
    }

    private static IReadOnlyList<StudentChatReportCitationDto> ParseCitations(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var citations = JsonSerializer.Deserialize<List<StudentChatCitationDto>>(json, JsonOptions) ?? [];
            return citations.Select((citation, index) => new StudentChatReportCitationDto(
                citation.CitationNumber > 0 ? citation.CitationNumber : index + 1,
                citation.DocumentTitle,
                citation.SubjectCode,
                citation.SubjectName,
                citation.ChapterTitle,
                citation.ChunkIndex,
                citation.SimilarityScore)).ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static int? ParseRetrievalCount(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("retrievalCount", out var value) && value.TryGetInt32(out var count)
                ? count
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class MutableTurn
    {
        public StudentChatReportMessageRecord? Prompt { get; init; }
        public StudentChatReportMessageRecord? Answer { get; set; }
        public StudentChatReportUsageRecord? Usage { get; set; }
    }
}
