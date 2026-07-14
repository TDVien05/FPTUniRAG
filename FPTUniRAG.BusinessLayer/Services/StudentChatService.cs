using System.Text;
using System.Text.Json;
using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.DataAccessLayer.Entities;
using FPTUniRAG.BusinessLayer.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FPTUniRAG.BusinessLayer.Services;

public sealed class StudentChatService : IStudentChatService
{
    private const int RetrievalLimit = 8;
    private const int ConversationHistoryLimit = 12;
    private const int StreamFlushIntervalMilliseconds = 40;
    private const int StreamFlushCharacterThreshold = 48;
    private const int MaxContextCharacters = 9000;
    private const int MaxChunkContextCharacters = 1400;
    private static readonly JsonSerializerOptions CitationSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _dbContext;
    private readonly IStudentChunkRetrievalService _retrievalService;
    private readonly IOpenRouterChatCompletionService _chatCompletionService;
    private readonly ILogger<StudentChatService> _logger;
    private readonly IFreeTokenQuotaService _freeTokenQuotaService;
    private readonly string _embeddingTableName;

    public StudentChatService(
        AppDbContext dbContext,
        IStudentChunkRetrievalService retrievalService,
        IOpenRouterChatCompletionService chatCompletionService,
        IOptions<RagIngestionOptions> options,
        ILogger<StudentChatService> logger,
        IFreeTokenQuotaService freeTokenQuotaService)
    {
        _dbContext = dbContext;
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

        var sessions = await _dbContext.Sessions
            .AsNoTracking()
            .Where(session => session.UserId == userId && session.SubjectId == subjectId.Value)
            .Select(session => new
            {
                session.SessionId,
                SubjectId = session.SubjectId!.Value,
                SubjectCode = session.Subject != null ? session.Subject.SubjectCode : string.Empty,
                SubjectName = session.Subject != null ? session.Subject.SubjectName : string.Empty,
                session.StartedAt,
                LastMessageAt = session.Messages.Max(message => message.CreatedAt),
                PreviewText = session.Messages
                    .OrderByDescending(message => message.CreatedAt)
                    .Select(message => message.MessageContent)
                    .FirstOrDefault()
            })
            .OrderByDescending(session => session.LastMessageAt ?? session.StartedAt)
            .ToListAsync(cancellationToken);

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
        var session = await _dbContext.Sessions
            .AsNoTracking()
            .Where(item => item.SessionId == sessionId && item.UserId == userId && item.SubjectId != null)
            .Select(item => new
            {
                item.SessionId,
                SubjectId = item.SubjectId!.Value,
                SubjectCode = item.Subject != null ? item.Subject.SubjectCode : string.Empty,
                SubjectName = item.Subject != null ? item.Subject.SubjectName : string.Empty,
                item.StartedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (session is null)
        {
            return null;
        }

        var messages = await _dbContext.Messages
            .AsNoTracking()
            .Where(message => message.SessionId == sessionId)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.MessageId)
            .Select(message => new
            {
                message.MessageId,
                message.SenderRole,
                message.MessageContent,
                message.CreatedAt,
                message.CitationsJson
            })
            .ToListAsync(cancellationToken);

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
        var ownedSession = await _dbContext.Sessions
            .AsNoTracking()
            .Where(session => session.SessionId == sessionId && session.UserId == userId && session.SubjectId != null)
            .Select(session => new
            {
                session.SessionId,
                SubjectId = session.SubjectId!.Value
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (ownedSession is null)
        {
            return null;
        }

        var chunk = await _dbContext.Chunks
            .AsNoTracking()
            .Where(item =>
                item.DocumentId == documentId
                && item.ChunkIndex == chunkIndex
                && item.Document.SubjectId == ownedSession.SubjectId
                && (item.Document.Status ?? string.Empty).ToLower() == "completed")
            .Select(item => new
            {
                item.ChunkId,
                item.DocumentId,
                item.Document.Title,
                item.Document.Subject.SubjectCode,
                item.Document.Subject.SubjectName,
                item.Document.Chapter.ChapterTitle,
                item.ChunkIndex,
                item.Content
            })
            .FirstOrDefaultAsync(cancellationToken);

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

        var subjectQuery = _dbContext.Subjects
            .AsNoTracking()
            .OrderBy(subject => subject.SubjectCode)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            subjectQuery = subjectQuery.Where(subject =>
                EF.Functions.ILike(subject.SubjectCode, $"%{normalizedQuery}%")
                || EF.Functions.ILike(subject.SubjectName, $"%{normalizedQuery}%")
                || (subject.Description != null && EF.Functions.ILike(subject.Description, $"%{normalizedQuery}%")));
        }

        var subjects = await subjectQuery
            .Take(50)
            .Select(subject => new StudentSubjectOptionDto(
                subject.SubjectId,
                subject.SubjectCode,
                subject.SubjectName,
                subject.Description,
                false))
            .ToListAsync(cancellationToken);

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

        StudentChatSubjectContext? subject;
        try
        {
            subject = await _dbContext.Subjects
                .AsNoTracking()
                .Where(candidate => candidate.SubjectId == request.SubjectId.Value)
                .Select(candidate => new StudentChatSubjectContext(
                    candidate.SubjectId,
                    candidate.SubjectCode,
                    candidate.SubjectName))
                .FirstOrDefaultAsync(cancellationToken);
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
                existingSession = await _dbContext.Sessions
                    .FirstOrDefaultAsync(
                        session => session.SessionId == request.SessionId.Value && session.UserId == userId,
                        cancellationToken);

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

                previousMessages = await _dbContext.Messages
                    .AsNoTracking()
                    .Where(message => message.SessionId == existingSession.SessionId)
                    .OrderByDescending(message => message.CreatedAt)
                    .Take(ConversationHistoryLimit)
                    .OrderBy(message => message.CreatedAt)
                    .ToListAsync(cancellationToken);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to load existing session {SessionId} for student chat user {UserId}", request.SessionId, userId);
            await writeEvent("error", new StudentChatErrorDto("Unable to load this chat session right now. Please try again."), cancellationToken);
            return;
        }

        bool hasUsableContent;
        IReadOnlyList<StudentRetrievedChunk> retrievedChunks;
        try
        {
            hasUsableContent = await _retrievalService.SubjectHasUsableContentAsync(subject.SubjectId, cancellationToken);
            if (!hasUsableContent)
            {
                await writeEvent("error", new StudentChatErrorDto("This subject does not have usable materials yet. Please choose another subject."), cancellationToken);
                return;
            }

            retrievedChunks = await _retrievalService.RetrieveRelevantChunksAsync(
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

        if (retrievedChunks.Count == 0)
        {
            await writeEvent("error", new StudentChatErrorDto("This subject does not have usable materials yet. Please choose another subject."), cancellationToken);
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
            _dbContext.Sessions.Add(session);
        }

        var userMessage = new Message
        {
            MessageId = Guid.NewGuid(),
            SessionId = session.SessionId,
            SenderRole = "student",
            MessageContent = normalizedMessage,
            CreatedAt = CreateDatabaseTimestamp()
        };

        _dbContext.Messages.Add(userMessage);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
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

        OpenRouterChatResult completion;
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
            _dbContext.Messages.Add(assistantMessage);

            _dbContext.TokenUsageLogs.Add(new TokenUsageLog
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
                MetadataJson = JsonSerializer.Serialize(new
                {
                    subject.SubjectId,
                    subject.SubjectCode,
                    retrievalCount = retrievedChunks.Count
                })
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
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
        await using var command = _dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            $"""
            SELECT DISTINCT d.subject_id
            FROM {_embeddingTableName} ce
            INNER JOIN chunks c ON c.chunk_id = ce.chunk_id
            INNER JOIN documents d ON d.document_id = c.document_id
            WHERE lower(coalesce(d.status, '')) = 'completed';
            """;

        if (command.Connection is not null && command.Connection.State != System.Data.ConnectionState.Open)
        {
            await command.Connection.OpenAsync(cancellationToken);
        }

        var subjectIds = new HashSet<Guid>();

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                subjectIds.Add(reader.GetGuid(0));
            }
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return subjectIds;
        }

        return subjectIds;
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
        var citationJsonCandidates = await _dbContext.Messages
            .AsNoTracking()
            .Where(message =>
                message.SessionId == sessionId
                && message.SenderRole == "assistant"
                && message.CitationsJson != null)
            .OrderByDescending(message => message.CreatedAt)
            .Select(message => message.CitationsJson)
            .ToListAsync(cancellationToken);

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
        var entitlement = await _dbContext.StudentActiveChatEntitlements
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);

        var usage = await _dbContext.StudentTokenUsageCurrentMonths
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);

        var monthlyTokenLimit = entitlement?.MonthlyTokenLimit is > 0
            ? entitlement.MonthlyTokenLimit.Value
            : await _freeTokenQuotaService.GetMonthlyTokenLimitAsync(cancellationToken);

        var tokensUsedThisMonth = usage?.TotalTokensUsedThisMonth ?? 0;
        return new StudentChatQuotaContext(
            entitlement?.PlanId,
            entitlement?.PlanCode,
            monthlyTokenLimit,
            tokensUsedThisMonth,
            Math.Max(0m, monthlyTokenLimit - tokensUsedThisMonth),
            entitlement?.PlanId is not null);
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
