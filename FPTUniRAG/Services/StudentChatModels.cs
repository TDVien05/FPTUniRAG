using System.ComponentModel.DataAnnotations;

namespace FPTUniRAG.Services;

public sealed record StudentChatPageDto(
    string StudentName,
    IReadOnlyList<StudentSubjectOptionDto> Subjects);

public sealed record StudentSubjectOptionDto(
    Guid SubjectId,
    string SubjectCode,
    string SubjectName,
    string? Description,
    bool HasUsableContent);

public sealed record StudentChatCitationDto(
    Guid DocumentId,
    string DocumentTitle,
    string SubjectCode,
    string SubjectName,
    string ChapterTitle,
    int ChunkIndex,
    double SimilarityScore);

public sealed record StudentChatMessageDto(
    Guid? MessageId,
    string SenderRole,
    string MessageContent,
    DateTime? CreatedAt,
    IReadOnlyList<StudentChatCitationDto> Citations);

public sealed record StudentChatSubjectSearchResponseDto(
    IReadOnlyList<StudentSubjectOptionDto> Subjects);

public sealed record StudentChatSessionStartedDto(
    Guid SessionId,
    Guid SubjectId);

public sealed record StudentChatAssistantDeltaDto(
    string Delta);

public sealed record StudentChatAssistantCompleteDto(
    Guid MessageId,
    string MessageContent,
    DateTime? CreatedAt,
    IReadOnlyList<StudentChatCitationDto> Citations);

public sealed record StudentChatErrorDto(
    string Message);

public sealed class StudentChatSendRequest
{
    public Guid? SessionId { get; set; }

    public Guid? SubjectId { get; set; }

    [Required(ErrorMessage = "Message is required.")]
    public string Message { get; set; } = string.Empty;
}

public sealed class StudentChatOperationResult<T>
{
    private StudentChatOperationResult(bool succeeded, string? errorMessage, T? value)
    {
        Succeeded = succeeded;
        ErrorMessage = errorMessage;
        Value = value;
    }

    public bool Succeeded { get; }

    public string? ErrorMessage { get; }

    public T? Value { get; }

    public static StudentChatOperationResult<T> Success(T value) => new(true, null, value);

    public static StudentChatOperationResult<T> Failure(string errorMessage) => new(false, errorMessage, default);
}

public sealed record StudentRetrievedChunk(
    Guid ChunkId,
    string Content,
    int ChunkIndex,
    Guid DocumentId,
    string DocumentTitle,
    string SubjectCode,
    string SubjectName,
    string ChapterTitle,
    double SimilarityScore);

public sealed record OpenRouterChatMessage(
    string Role,
    string Content);

public sealed record OpenRouterChatResult(
    string ModelName,
    string Content,
    long PromptTokens,
    long CompletionTokens,
    long TotalTokens,
    string? FinishReason,
    int StreamedCharacterCount,
    bool FallbackUsed);
