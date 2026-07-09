using System.Text;
using System.Text.Json;
using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.DataAccessLayer.Entities;
using FPTUniRAG.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FPTUniRAG.Services;

public sealed class StudentChatService : IStudentChatService
{
    private const int RetrievalLimit = 4;
    private const int ConversationHistoryLimit = 12;
    private static readonly JsonSerializerOptions CitationSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _dbContext;
    private readonly IStudentChunkRetrievalService _retrievalService;
    private readonly IOpenRouterChatCompletionService _chatCompletionService;
    private readonly string _embeddingTableName;

    public StudentChatService(
        AppDbContext dbContext,
        IStudentChunkRetrievalService retrievalService,
        IOpenRouterChatCompletionService chatCompletionService,
        IOptions<RagIngestionOptions> options)
    {
        _dbContext = dbContext;
        _retrievalService = retrievalService;
        _chatCompletionService = chatCompletionService;
        _embeddingTableName = ResolveTableName(options.Value.PostgresVector.TableName);
    }

    public async Task<StudentChatPageDto> GetDashboardAsync(
        Guid userId,
        string studentName,
        CancellationToken cancellationToken = default)
    {
        var subjects = await SearchSubjectsAsync(null, cancellationToken);
        return new StudentChatPageDto(studentName, subjects);
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

        var subject = await _dbContext.Subjects
            .AsNoTracking()
            .Where(candidate => candidate.SubjectId == request.SubjectId.Value)
            .Select(candidate => new
            {
                candidate.SubjectId,
                candidate.SubjectCode,
                candidate.SubjectName
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (subject is null)
        {
            await writeEvent("error", new StudentChatErrorDto("The selected subject no longer exists."), cancellationToken);
            return;
        }

        Session? existingSession = null;
        List<Message> previousMessages = [];
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

        if (!await _retrievalService.SubjectHasUsableContentAsync(subject.SubjectId, cancellationToken))
        {
            await writeEvent("error", new StudentChatErrorDto("This subject does not have usable materials yet. Please choose another subject."), cancellationToken);
            return;
        }

        var retrievedChunks = await _retrievalService.RetrieveRelevantChunksAsync(
            subject.SubjectId,
            normalizedMessage,
            RetrievalLimit,
            cancellationToken);

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

        var completionMessages = BuildCompletionMessages(
            subject.SubjectCode,
            subject.SubjectName,
            previousMessages,
            retrievedChunks,
            normalizedMessage);

        OpenRouterChatResult completion;
        try
        {
            completion = await _chatCompletionService.StreamCompletionAsync(
                completionMessages,
                async (delta, token) =>
                {
                    if (!string.IsNullOrEmpty(delta))
                    {
                        await writeEvent("assistant-delta", new StudentChatAssistantDeltaDto(delta), token);
                    }
                },
                cancellationToken);
        }
        catch (Exception)
        {
            await writeEvent("error", new StudentChatErrorDto("Unable to generate an answer right now. Please try again in a moment."), cancellationToken);
            return;
        }

        var citations = BuildCitations(retrievedChunks);
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

            var entitlement = await _dbContext.StudentActiveChatEntitlements
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);

            _dbContext.TokenUsageLogs.Add(new TokenUsageLog
            {
                TokenUsageId = Guid.NewGuid(),
                UserId = userId,
                SessionId = session.SessionId,
                MessageId = assistantMessage.MessageId,
                PlanId = entitlement?.PlanId,
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
        catch (Exception)
        {
            await writeEvent("error", new StudentChatErrorDto("The answer was generated but could not be saved. Please try again."), cancellationToken);
            return;
        }

        await writeEvent(
            "assistant-complete",
            new StudentChatAssistantCompleteDto(
                assistantMessage.MessageId,
                assistantMessage.MessageContent,
                assistantMessage.CreatedAt,
                citations),
            cancellationToken);
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

    private static IReadOnlyList<OpenRouterChatMessage> BuildCompletionMessages(
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
            """;

        var contextBuilder = new StringBuilder();
        contextBuilder.AppendLine($"Subject: {subjectCode} - {subjectName}");
        contextBuilder.AppendLine("Reference materials:");

        foreach (var chunk in retrievedChunks)
        {
            contextBuilder.AppendLine($"[Document: {chunk.DocumentTitle} | Chapter: {chunk.ChapterTitle} | Chunk: {chunk.ChunkIndex}]");
            contextBuilder.AppendLine(chunk.Content);
            contextBuilder.AppendLine();
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

        return messages;
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
            .GroupBy(chunk => new
            {
                chunk.DocumentId,
                chunk.DocumentTitle,
                chunk.SubjectCode,
                chunk.SubjectName,
                chunk.ChapterTitle
            })
            .Select(group => new StudentChatCitationDto(
                group.Key.DocumentId,
                group.Key.DocumentTitle,
                group.Key.SubjectCode,
                group.Key.SubjectName,
                group.Key.ChapterTitle,
                group.Min(item => item.ChunkIndex),
                Math.Round(group.Max(item => item.SimilarityScore), 4)))
            .OrderByDescending(item => item.SimilarityScore)
            .ThenBy(item => item.DocumentTitle)
            .Take(4)
            .ToList();
    }

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
