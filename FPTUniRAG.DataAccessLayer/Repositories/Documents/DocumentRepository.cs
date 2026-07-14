using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FPTUniRAG.DataAccessLayer.Repositories.Documents;

public sealed class DocumentRepository(AppDbContext context) : IDocumentRepository
{
    private IQueryable<TeacherSubject> Managed(string email) => context.TeacherSubjects.Where(l => l.IsHeadOfDepartment && l.Teacher.Email != null && l.Teacher.Email.ToLower() == email.Trim().ToLower());
    public Task<TeacherSubject?> GetManagedSubjectAsync(string email, Guid subjectId, CancellationToken token = default) => Managed(email).Include(l => l.Teacher).Include(l => l.Subject).ThenInclude(s => s.Chapters).Include(l => l.Subject).ThenInclude(s => s.Documents).FirstOrDefaultAsync(l => l.SubjectId == subjectId, token);
    public Task<Document?> GetManagedDocumentAsync(string email, Guid documentId, CancellationToken token = default) => Managed(email).SelectMany(l => l.Subject.Documents).Include(d => d.Subject).Include(d => d.Chapter).Include(d => d.Chunks).Include(d => d.ProcessingJobs).FirstOrDefaultAsync(d => d.DocumentId == documentId, token);
    public Task<bool> ManagesSubjectAsync(string email, Guid subjectId, CancellationToken token = default) => Managed(email).AnyAsync(l => l.SubjectId == subjectId, token);
    public Task<Chapter?> FindChapterAsync(Guid subjectId, string title, CancellationToken token = default) => context.Chapters.FirstOrDefaultAsync(c => c.SubjectId == subjectId && c.ChapterTitle.ToLower() == title.ToLower(), token);
    public Task<bool> ChapterHasDocumentAsync(Guid chapterId, CancellationToken token = default) => context.Documents.AnyAsync(d => d.ChapterId == chapterId, token);
    public async Task<Chapter> CreateChapterAsync(Guid subjectId, string title, DateTime createdAt, CancellationToken token = default)
    { var order = await context.Chapters.Where(c => c.SubjectId == subjectId).MaxAsync(c => (int?)c.ChapterOrder, token) ?? 0; var chapter = new Chapter { ChapterId = Guid.NewGuid(), SubjectId = subjectId, ChapterTitle = title, ChapterOrder = order + 1, CreatedAt = createdAt }; context.Chapters.Add(chapter); await context.SaveChangesAsync(token); return chapter; }
    public async Task AddDocumentAsync(Document document, ProcessingJob job, CancellationToken token = default) { context.Documents.Add(document); context.ProcessingJobs.Add(job); await context.SaveChangesAsync(token); }
    public Task<bool> HasChunksAsync(Guid documentId, CancellationToken token = default) => context.Chunks.AnyAsync(c => c.DocumentId == documentId, token);
    public async Task QueueDocumentAsync(Document document, ProcessingJob job, CancellationToken token = default) { if (context.Entry(job).State == EntityState.Detached) context.ProcessingJobs.Add(job); await context.SaveChangesAsync(token); }
    public Task<Chapter?> GetChapterWithDocumentAsync(Guid subjectId, Guid chapterId, CancellationToken token = default) => context.Chapters.Include(c => c.Document).FirstOrDefaultAsync(c => c.ChapterId == chapterId && c.SubjectId == subjectId, token);
    public async Task DeleteChapterAsync(Guid chapterId, Guid? documentId, string tableName, CancellationToken token = default)
    {
        ValidateTable(tableName); await using var transaction = await context.Database.BeginTransactionAsync(token);
        await context.TestQuestions.Where(q => q.ChapterId == chapterId).ExecuteDeleteAsync(token);
        if (documentId.HasValue)
        {
#pragma warning disable EF1002
            await context.Database.ExecuteSqlRawAsync($"DELETE FROM {tableName} WHERE chunk_id IN (SELECT chunk_id FROM chunks WHERE document_id=@documentId)", [new NpgsqlParameter<Guid>("documentId", documentId.Value)], token);
#pragma warning restore EF1002
            await context.DocumentEmbeddingRuns.Where(r => r.DocumentId == documentId).ExecuteDeleteAsync(token);
            await context.ProcessingJobs.Where(j => j.DocumentId == documentId).ExecuteDeleteAsync(token);
            await context.Chunks.Where(c => c.DocumentId == documentId).ExecuteDeleteAsync(token);
            await context.Documents.Where(d => d.DocumentId == documentId).ExecuteDeleteAsync(token);
        }
        await context.Chapters.Where(c => c.ChapterId == chapterId).ExecuteDeleteAsync(token); await transaction.CommitAsync(token);
    }
    public Task<Document?> GetDocumentForProcessingAsync(Guid id, CancellationToken token = default) => context.Documents.Include(d => d.Subject).Include(d => d.Chapter).Include(d => d.ProcessingJobs).FirstOrDefaultAsync(d => d.DocumentId == id, token);
    public async Task<IReadOnlyList<Chunk>> GetChunksAsync(Guid id, CancellationToken token = default) => await context.Chunks.Where(c => c.DocumentId == id).OrderBy(c => c.ChunkIndex).ToListAsync(token);
    public async Task ReplaceChunksAsync(Guid id, IReadOnlyList<Chunk> chunks, CancellationToken token = default) { await context.Chunks.Where(c => c.DocumentId == id).ExecuteDeleteAsync(token); context.Chunks.AddRange(chunks); await context.SaveChangesAsync(token); }
    public async Task AddEmbeddingRunAsync(DocumentEmbeddingRun run, CancellationToken token = default) { context.DocumentEmbeddingRuns.Add(run); await context.SaveChangesAsync(token); }
    public Task SaveProcessingStateAsync(CancellationToken token = default) => context.SaveChangesAsync(token);
    public async Task<IReadOnlyList<Guid>> GetPendingDocumentIdsAsync(CancellationToken token = default) => await context.ProcessingJobs.Where(j => j.JobStatus == "queued" || j.JobStatus == "processing").Select(j => j.DocumentId).Distinct().ToListAsync(token);
    private static void ValidateTable(string value) { if (!value.All(c => char.IsLetterOrDigit(c) || c == '_')) throw new InvalidOperationException("Invalid embedding table name."); }
}
