using FPTUniRAG.BusinessLayer.Rag.Chunking;
using System.ComponentModel.DataAnnotations;

namespace FPTUniRAG.BusinessLayer.Rag.Ingestion;

public sealed record TeacherUploadContextDto(
    Guid SubjectId,
    string SubjectCode,
    string SubjectName,
    string? SubjectDescription,
    string DefaultChunkingStrategy,
    int DefaultFixedChunkSize,
    IReadOnlyList<string> AllowedFileTypes);

public sealed record TeacherDocumentChunkDto(
    Guid ChunkId,
    int ChunkIndex,
    string Content)
{
    public int CountedCharacterCount => ChunkCharacterCounter.Count(Content);
}

public sealed record TeacherDocumentDetailDto(
    Guid DocumentId,
    Guid SubjectId,
    string SubjectCode,
    string SubjectName,
    string ChapterTitle,
    string Title,
    string FileType,
    string FileUrl,
    string Status,
    string ChunkingStrategy,
    int ChunkSize,
    int ChunkOverlap,
    int ChunkCount,
    DateTime? CreatedAt,
    IReadOnlyList<TeacherDocumentChunkDto> Chunks);

public sealed record TeacherDocumentUploadResult(
    bool Succeeded,
    string Message,
    Guid? DocumentId);

public sealed record TeacherDocumentProcessingStatusDto(
    Guid DocumentId,
    string Status,
    int ProgressPercent,
    string Stage,
    string? ErrorMessage,
    bool IsCompleted,
    bool IsFailed,
    int? QueuePosition,
    int QueueDepth);

public sealed class TeacherDocumentUploadCommand
{
    public Guid SubjectId { get; set; }

    [Required(ErrorMessage = "Document title is required.")]
    [StringLength(255)]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Chapter name is required.")]
    [StringLength(255)]
    public string ChapterTitle { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please choose a file to upload.")]
    public IFormFile? File { get; set; }
}
