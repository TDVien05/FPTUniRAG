using FPTUniRAG.BusinessLayer.Rag.Configuration;
using FPTUniRAG.BusinessLayer.Subscriptions;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FPTUniRAG.DataAccessLayer.Repositories.Chat;
using FPTUniRAG.DataAccessLayer.Repositories.Embeddings;
using FPTUniRAG.DataAccessLayer.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FPTUniRAG.BusinessLayer.Rag.Chat;

public sealed class StudentChatService : IStudentChatService
{
    private const int RetrievalLimit = 8;
    private const int ConversationHistoryLimit = 12;
    private const int StreamFlushIntervalMilliseconds = 40;
    private const int StreamFlushCharacterThreshold = 48;
    private const int MaxContextCharacters = 9000;
    private const int MaxChunkContextCharacters = 1400;
    private static readonly JsonSerializerOptions CitationSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IStudentChatRepository _chatRepository;
    private readonly IChunkVectorRepository _vectorRepository;
    private readonly IStudentChunkRetrievalService _retrievalService;
    private readonly IOpenRouterChatCompletionService _chatCompletionService;
    private readonly ILogger<StudentChatService> _logger;
    private readonly IFreeTokenQuotaService _freeTokenQuotaService;
    private readonly string _embeddingTableName;

    public StudentChatService(
        IStudentChatRepository chatRepository,
        IChunkVectorRepository vectorRepository,
        IStudentChunkRetrievalService retrievalService,
        IOpenRouterChatCompletionService chatCompletionService,
        IOptions<RagIngestionOptions> options,
        ILogger<StudentChatService> logger,
        IFreeTokenQuotaService freeTokenQuotaService)
    {
        _chatRepository = chatRepository;
        _vectorRepository = vectorRepository;
        _retrievalService = retrievalService;
        _chatCompletionService = chatCompletionService;
        _logger = logger;
        _freeTokenQuotaService = freeTokenQuotaService;
        _embeddingTableName = ResolveTableName(options.Value.PostgresVector.TableName);
    }

    public async Task<StudentChatPageDto> GetDashboardAsync(
        Guid userId,
        string studentName,
        CancellationToken cancellationToken = default)
    {
        var subjects = await SearchSubjectsAsync(null, cancellationToken);
        return new StudentChatPageDto(studentName, subjects, []);
    }

    public async Task<IReadOnlyList<StudentChatSessionSummaryDto>> GetSessionsAsync(
        Guid userId,
        Guid? subjectId,
        CancellationToken cancellationToken = default)
    {
        if (subjectId is null || subjectId == Guid.Empty)
        {
            return [];
        }

        var sessions = await _chatRepository.GetSessionsAsync(userId, subjectId.Value, cancellationToken);

        return sessions
            .Select(session => new StudentChatSessionSummaryDto(
                session.SessionId,
                session.SubjectId,
                session.SubjectCode,
                session.SubjectName,
                session.StartedAt,
                session.LastMessageAt,
                BuildPreviewText(session.PreviewText)))
            .ToList();
    }

    public async Task<StudentChatSessionDetailDto?> GetSessionDetailAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _chatRepository.GetSessionAsync(userId, sessionId, cancellationToken);

        if (session is null)
        {
            return null;
        }

        var messages = await _chatRepository.GetMessagesAsync(sessionId, cancellationToken);

        return new StudentChatSessionDetailDto(
            session.SessionId,
            session.SubjectId,
            session.SubjectCode,
            session.SubjectName,
            session.StartedAt,
            messages.Select(message => new StudentChatMessageDto(
                message.MessageId,
                message.SenderRole,
                message.MessageContent,
                message.CreatedAt,
                DeserializeCitations(message.CitationsJson)))
            .ToList());
    }

    public async Task<StudentChatCitationDetailDto?> GetCitationDetailAsync(
        Guid userId,
        Guid sessionId,
        Guid documentId,
        int chunkIndex,
        CancellationToken cancellationToken = default)
    {
        var chunk = await _chatRepository.GetCitationChunkAsync(
            userId, sessionId, documentId, chunkIndex, cancellationToken);

        if (chunk is null)
        {
            return null;
        }

        var similarityScore = await TryResolveCitationSimilarityAsync(
            sessionId,
            documentId,
            chunkIndex,
            cancellationToken);

        return new StudentChatCitationDetailDto(
            chunk.DocumentId,
            chunk.Title,
            chunk.SubjectCode,
            chunk.SubjectName,
            chunk.ChapterTitle,
            chunk.ChunkIndex,
            chunk.Content,
            similarityScore,
            chunk.ChunkId);
    }

    public async Task<IReadOnlyList<StudentSubjectOptionDto>> SearchSubjectsAsync(
        string? query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query?.Trim();
        var availableSubjectIds = await GetUsableSubjectIdsAsync(cancellationToken);

        var records = await _chatRepository.SearchSubjectsAsync(normalizedQuery, 50, cancellationToken);
        var subjects = records.Select(subject => new StudentSubjectOptionDto(
            subject.SubjectId, subject.SubjectCode, subject.SubjectName, subject.Description, false)).ToList();

        return subjects
            .Select(subject => subject with { HasUsableContent = availableSubjectIds.Contains(subject.SubjectId) })
            .ToList();
    }

    public async Task StreamMessageAsync(
        Guid userId,
        StudentChatSendRequest request,
        Func<string, object, CancellationToken, Task> writeEvent,
        CancellationToken cancellationToken = default)
    {
        var normalizedMessage = request.Message.Trim();
        if (request.SubjectId is null || request.SubjectId == Guid.Empty)
        {
            await writeEvent("error", new StudentChatErrorDto("Please choose a subject before chatting."), cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            await writeEvent("error", new StudentChatErrorDto("Please enter a message before sending."), cancellationToken);
            return;
        }

        await WriteProgressAsync("validating-request", writeEvent, cancellationToken);

        StudentChatSubjectContext? subject;
        try
        {
            var record = await _chatRepository.GetSubjectAsync(request.SubjectId.Value, cancellationToken);
            subject = record is null ? null : new StudentChatSubjectContext(record.SubjectId, record.SubjectCode, record.SubjectName);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to load subject {SubjectId} for student chat user {UserId}", request.SubjectId, userId);
            await writeEvent("error", new StudentChatErrorDto("Unable to load the selected subject right now. Please try again."), cancellationToken);
            return;
        }

        if (subject is null)
        {
            await writeEvent("error", new StudentChatErrorDto("The selected subject no longer exists."), cancellationToken);
            return;
        }

        StudentChatQuotaContext quotaContext;
        try
        {
            quotaContext = await GetQuotaContextAsync(userId, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to resolve token quota for student chat user {UserId}", userId);
            await writeEvent("error", new StudentChatErrorDto("Unable to verify your token allowance right now. Please try again."), cancellationToken);
            return;
        }

        if (quotaContext.TokensRemainingThisMonth <= 0)
        {
            var quotaExceededMessage = quotaContext.HasPaidPlan
                ? $"You have used all {quotaContext.MonthlyTokenLimit:N0} tokens in your current plan this month. Please upgrade your subscription plan to continue."
                : $"You have used all {quotaContext.MonthlyTokenLimit:N0} free tokens. Please purchase a subscription plan to continue chatting.";

            await writeEvent("error", new StudentChatErrorDto(quotaExceededMessage), cancellationToken);
            return;
        }

        Session? existingSession = null;
        List<Message> previousMessages = [];
        try
        {
            if (request.SessionId is not null && request.SessionId != Guid.Empty)
            {
                existingSession = await _chatRepository.FindOwnedSessionAsync(userId, request.SessionId.Value, cancellationToken);

                if (existingSession is null)
                {
                    await writeEvent("error", new StudentChatErrorDto("The selected chat session is no longer available."), cancellationToken);
                    return;
                }

                if (existingSession.SubjectId != subject.SubjectId)
                {
                    await writeEvent("error", new StudentChatErrorDto("This chat session belongs to a different subject."), cancellationToken);
                    return;
                }

                previousMessages = (await _chatRepository.GetRecentMessagesAsync(
                    existingSession.SessionId, ConversationHistoryLimit, cancellationToken)).ToList();
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to load existing session {SessionId} for student chat user {UserId}", request.SessionId, userId);
            await writeEvent("error", new StudentChatErrorDto("Unable to load this chat session right now. Please try again."), cancellationToken);
            return;
        }

        await WriteProgressAsync("searching-materials", writeEvent, cancellationToken);

        bool hasUsableContent;
        StudentChunkRetrievalResult retrievalResult;
        try
        {
            hasUsableContent = await _retrievalService.SubjectHasUsableContentAsync(subject.SubjectId, cancellationToken);
            if (!hasUsableContent)
            {
                await writeEvent("error", new StudentChatErrorDto("This subject does not have any completed, embedded materials yet. Please choose another subject or ask your teacher to check document processing."), cancellationToken);
                return;
            }

            retrievalResult = await _retrievalService.RetrieveRelevantChunksAsync(
                subject.SubjectId,
                normalizedMessage,
                RetrievalLimit,
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to retrieve supporting chunks for subject {SubjectId} and student chat user {UserId}", subject.SubjectId, userId);
            await writeEvent("error", new StudentChatErrorDto("Unable to search the subject materials right now. Please try again in a moment."), cancellationToken);
            return;
        }

        var retrievedChunks = retrievalResult.Chunks;
        if (retrievedChunks.Count == 0)
        {
            var message = retrievalResult.UsedLexicalFallback
                ? "Semantic search is temporarily unavailable, and no matching text was found. Please try again in a moment."
                : "No relevant material was found for this question. Please try rephrasing it.";
            await writeEvent("error", new StudentChatErrorDto(message), cancellationToken);
            return;
        }

        var session = existingSession;
        if (session is null)
        {
            session = new Session
            {
                SessionId = Guid.NewGuid(),
                UserId = userId,
                SubjectId = subject.SubjectId,
                StartedAt = CreateDatabaseTimestamp()
            };
        }

        var userMessage = new Message
        {
            MessageId = Guid.NewGuid(),
            SessionId = session.SessionId,
            SenderRole = "student",
            MessageContent = normalizedMessage,
            CreatedAt = CreateDatabaseTimestamp()
        };

        try
        {
            await _chatRepository.SaveUserMessageAsync(existingSession is null ? session : null, userMessage, cancellationToken);
        }
        catch (Exception)
        {
            await writeEvent("error", new StudentChatErrorDto("Unable to start this chat message right now. Please try again."), cancellationToken);
            return;
        }

        await writeEvent(
            "session-started",
            new StudentChatSessionStartedDto(session.SessionId, subject.SubjectId),
            cancellationToken);

        await writeEvent(
            "user-message-ack",
            new StudentChatMessageDto(
                userMessage.MessageId,
                userMessage.SenderRole,
                userMessage.MessageContent,
                userMessage.CreatedAt,
                []),
            cancellationToken);

        var completionContext = BuildCompletionMessages(
            subject.SubjectCode,
            subject.SubjectName,
            previousMessages,
            retrievedChunks,
            normalizedMessage);

        await WriteProgressAsync("generating-answer", writeEvent, cancellationToken);

        OpenRouterChatResult completion;
        var completionStopwatch = Stopwatch.StartNew();
        try
        {
            completion = await _chatCompletionService.StreamCompletionAsync(
                completionContext.Messages,
                (_, _) =>
                {
                    return Task.CompletedTask;
                },
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to generate chat completion for subject {SubjectId} and student chat user {UserId}", subject.SubjectId, userId);
            await writeEvent("error", new StudentChatErrorDto("Unable to generate an answer right now. Please try again in a moment."), cancellationToken);
            return;
        }
        finally
        {
            completionStopwatch.Stop();
        }

        await WriteProgressAsync("finalizing-answer", writeEvent, cancellationToken);

        var citations = BuildCitations(completionContext.ReferencedChunks);
        var assistantMessage = new Message
        {
            MessageId = Guid.NewGuid(),
            SessionId = session.SessionId,
            SenderRole = "assistant",
            MessageContent = completion.Content,
            CitationsJson = JsonSerializer.Serialize(citations, CitationSerializerOptions),
            CreatedAt = CreateDatabaseTimestamp()
        };

        try
        {
            var usageLog = new TokenUsageLog
            {
                TokenUsageId = Guid.NewGuid(),
                UserId = userId,
                SessionId = session.SessionId,
                MessageId = assistantMessage.MessageId,
                PlanId = quotaContext.PlanId,
                FeatureName = "student_chat",
                ProviderName = "openrouter",
                ModelName = completion.ModelName,
                PromptTokens = completion.PromptTokens,
                CompletionTokens = completion.CompletionTokens,
                TotalTokens = completion.TotalTokens,
                RequestCount = 1,
                UsedAt = CreateDatabaseTimestamp(),
                ResponseTimeMs = (int)Math.Min(completionStopwatch.ElapsedMilliseconds, int.MaxValue),
                MetadataJson = JsonSerializer.Serialize(new
                {
                    subject.SubjectId,
                    subject.SubjectCode,
                    retrievalCount = retrievedChunks.Count
                })
            };
            await _chatRepository.SaveAssistantResponseAsync(assistantMessage, usageLog, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to persist assistant response for session {SessionId} and student chat user {UserId}", session.SessionId, userId);
            await writeEvent("error", new StudentChatErrorDto("The answer was generated but could not be saved. Please try again."), cancellationToken);
            return;
        }

        await StreamCompletedAssistantResponseAsync(completion.Content, writeEvent, cancellationToken);

        await writeEvent(
            "assistant-complete",
            new StudentChatAssistantCompleteDto(
                assistantMessage.MessageId,
                assistantMessage.MessageContent,
                assistantMessage.CreatedAt,
                citations),
            cancellationToken);
    }

    private static Task WriteProgressAsync(
        string stage,
        Func<string, object, CancellationToken, Task> writeEvent,
        CancellationToken cancellationToken) =>
        writeEvent("processing-progress", new StudentChatProgressDto(stage), cancellationToken);

    private static async Task StreamCompletedAssistantResponseAsync(
        string content,
        Func<string, object, CancellationToken, Task> writeEvent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        for (var offset = 0; offset < content.Length; offset += StreamFlushCharacterThreshold)
        {
            var length = Math.Min(StreamFlushCharacterThreshold, content.Length - offset);
            await writeEvent(
                "assistant-delta",
                new StudentChatAssistantDeltaDto(content.Substring(offset, length)),
                cancellationToken);

            if (offset + length < content.Length)
            {
                await Task.Delay(StreamFlushIntervalMilliseconds, cancellationToken);
            }
        }
    }

    private async Task<HashSet<Guid>> GetUsableSubjectIdsAsync(CancellationToken cancellationToken)
    {
        return (await _vectorRepository.GetUsableSubjectIdsAsync(_embeddingTableName, cancellationToken)).ToHashSet();
    }

    private static StudentChatPromptContext BuildCompletionMessages(
        string subjectCode,
        string subjectName,
        IReadOnlyList<Message> previousMessages,
        IReadOnlyList<StudentRetrievedChunk> retrievedChunks,
        string userMessage)
    {
        var systemPrompt =
            """
            You are FPT UniRAG, an academic tutor for university students.
            Answer in the same language as the student's question.
            Base the answer on the provided subject materials and treat them as the primary source of truth.
            If the materials are insufficient for a confident answer, say clearly what is missing from the uploaded materials instead of inventing facts.

            Response style requirements:
            1. Start with a direct answer or conclusion in 2 to 4 sentences.
            2. Continue with a detailed explanation using structured markdown.
            3. Prefer sections such as "Explanation", "Key points", "Example", "How to apply", or equivalent natural headings in the student's language.
            4. Explain important terms in simple language before going deeper.
            5. Include at least one concrete example, analogy, or short scenario when it helps understanding.
            6. If the question asks for comparison, provide a clear side-by-side comparison.
            7. If the question is procedural, provide ordered steps.
            8. End with a short takeaway or summary sentence.

            Quality bar:
            - Be more detailed than a short summary.
            - For broad conceptual questions, aim for a complete answer around 220 to 450 words unless the user explicitly asks for brevity.
            - Do not answer with only one brief paragraph unless the question is extremely simple.
            - Avoid vague filler. Each paragraph should add useful information.
            - Mention the strongest referenced documents naturally when relevant, but do not turn the answer into a bibliography.
            - Each reference material is labeled [Source N].
            - Add inline citations such as [1], [2], or [1][3] immediately after the factual statement they support.
            - Cite important definitions, numbers, procedures, comparisons, and conclusions.
            - Use at least 2 different sources when the provided materials contain at least 2 relevant sources.
            - Never invent a citation number and never cite a source that does not support the statement.
            """;

        var contextBuilder = new StringBuilder();
        contextBuilder.AppendLine($"Subject: {subjectCode} - {subjectName}");
        contextBuilder.AppendLine("Reference materials:");
        var referencedChunks = new List<StudentRetrievedChunk>();

        var remainingContextCharacters = MaxContextCharacters;
        foreach (var chunk in retrievedChunks)
        {
            if (remainingContextCharacters <= 0)
            {
                break;
            }

            var excerpt = BuildChunkExcerpt(chunk.Content, Math.Min(MaxChunkContextCharacters, remainingContextCharacters));
            if (string.IsNullOrWhiteSpace(excerpt))
            {
                continue;
            }

            referencedChunks.Add(chunk);
            contextBuilder.AppendLine($"[Source {referencedChunks.Count} | Document: {chunk.DocumentTitle} | Chapter: {chunk.ChapterTitle} | Chunk: {chunk.ChunkIndex}]");
            contextBuilder.AppendLine(excerpt);
            contextBuilder.AppendLine();
            remainingContextCharacters -= excerpt.Length;
        }

        var messages = new List<OpenRouterChatMessage>
        {
            new("system", systemPrompt)
        };

        foreach (var previousMessage in previousMessages)
        {
            messages.Add(new OpenRouterChatMessage(
                NormalizeChatRole(previousMessage.SenderRole),
                previousMessage.MessageContent));
        }

        messages.Add(new OpenRouterChatMessage(
            "user",
            $"{contextBuilder}\nStudent question: {userMessage}"));

        return new StudentChatPromptContext(messages, referencedChunks);
    }

    private static string NormalizeChatRole(string? senderRole)
    {
        return string.Equals(senderRole, "assistant", StringComparison.OrdinalIgnoreCase)
            ? "assistant"
            : "user";
    }

    private static IReadOnlyList<StudentChatCitationDto> BuildCitations(IReadOnlyList<StudentRetrievedChunk> retrievedChunks)
    {
        return retrievedChunks
            .Select((chunk, index) => new StudentChatCitationDto(
                chunk.DocumentId,
                chunk.DocumentTitle,
                chunk.SubjectCode,
                chunk.SubjectName,
                chunk.ChapterTitle,
                chunk.ChunkIndex,
                Math.Round(chunk.SimilarityScore, 4),
                chunk.ChunkId,
                index + 1))
            .ToList();
    }

    private sealed record StudentChatPromptContext(
        IReadOnlyList<OpenRouterChatMessage> Messages,
        IReadOnlyList<StudentRetrievedChunk> ReferencedChunks);

    private static IReadOnlyList<StudentChatCitationDto> DeserializeCitations(string? citationsJson)
    {
        if (string.IsNullOrWhiteSpace(citationsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<StudentChatCitationDto>>(citationsJson, CitationSerializerOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string BuildPreviewText(string? content)
    {
        var normalized = string.IsNullOrWhiteSpace(content)
            ? "Untitled chat"
            : content.Trim().Replace("\r", " ").Replace("\n", " ");

        return normalized.Length <= 120
            ? normalized
            : normalized[..117] + "...";
    }

    private static string BuildChunkExcerpt(string? content, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(content) || maxLength <= 0)
        {
            return string.Empty;
        }

        var normalized = content.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..Math.Max(0, maxLength - 3)].TrimEnd() + "...";
    }

    private async Task<double> TryResolveCitationSimilarityAsync(
        Guid sessionId,
        Guid documentId,
        int chunkIndex,
        CancellationToken cancellationToken)
    {
        var citationJsonCandidates = await _chatRepository.GetCitationJsonAsync(sessionId, cancellationToken);

        foreach (var citationJson in citationJsonCandidates)
        {
            var citations = DeserializeCitations(citationJson);
            var match = citations.FirstOrDefault(citation =>
                citation.DocumentId == documentId
                && citation.ChunkIndex == chunkIndex);

            if (match is not null)
            {
                return match.SimilarityScore;
            }
        }

        return 0;
    }

    private sealed record StudentChatSubjectContext(
        Guid SubjectId,
        string SubjectCode,
        string SubjectName);

    private async Task<StudentChatQuotaContext> GetQuotaContextAsync(Guid userId, CancellationToken cancellationToken)
    {
        var quota = await _chatRepository.GetQuotaAsync(userId, cancellationToken);

        var monthlyTokenLimit = quota.MonthlyTokenLimit is > 0
            ? quota.MonthlyTokenLimit.Value
            : await _freeTokenQuotaService.GetMonthlyTokenLimitAsync(cancellationToken);

        var tokensUsedThisMonth = quota.TokensUsedThisMonth;
        return new StudentChatQuotaContext(
            quota.PlanId,
            quota.PlanCode,
            monthlyTokenLimit,
            tokensUsedThisMonth,
            Math.Max(0m, monthlyTokenLimit - tokensUsedThisMonth),
            quota.PlanId is not null);
    }

    private sealed record StudentChatQuotaContext(
        Guid? PlanId,
        string? PlanCode,
        long MonthlyTokenLimit,
        decimal TokensUsedThisMonth,
        decimal TokensRemainingThisMonth,
        bool HasPaidPlan);

    private static DateTime CreateDatabaseTimestamp()
    {
        return DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
    }

    private static string ResolveTableName(string? configuredTableName)
    {
        var tableName = string.IsNullOrWhiteSpace(configuredTableName)
            ? "chunk_embeddings"
            : configuredTableName.Trim();

        if (!tableName.All(character => char.IsLetterOrDigit(character) || character == '_'))
        {
            throw new InvalidOperationException("RagIngestion:PostgresVector:TableName must contain only letters, digits, or underscores.");
        }

        return tableName;
    }
}
