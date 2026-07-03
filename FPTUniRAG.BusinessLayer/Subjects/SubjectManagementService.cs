using FPTUniRAG.BusinessLayer.Accounts;
using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;

namespace FPTUniRAG.BusinessLayer.Subjects;

public sealed class SubjectManagementService : ISubjectManagementService
{
    private readonly AppDbContext _dbContext;

    public SubjectManagementService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<SubjectListItemDto>> GetSubjectsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Subjects
            .AsNoTracking()
            .OrderBy(subject => subject.SubjectCode)
            .Select(subject => new SubjectListItemDto(
                subject.SubjectId,
                subject.SubjectCode,
                subject.SubjectName,
                subject.Description,
                subject.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SubjectHeaderAssignmentListItemDto>> GetSubjectsForHeaderAssignmentAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Subjects
            .AsNoTracking()
            .OrderBy(subject => subject.SubjectCode)
            .Select(subject => new SubjectHeaderAssignmentListItemDto(
                subject.SubjectId,
                subject.SubjectCode,
                subject.SubjectName,
                subject.TeacherSubjects
                    .Where(link => link.IsHeadOfDepartment)
                    .Select(link => (Guid?)link.TeacherId)
                    .FirstOrDefault(),
                subject.TeacherSubjects
                    .Where(link => link.IsHeadOfDepartment)
                    .Select(link => link.Teacher.FullName)
                    .FirstOrDefault(),
                subject.TeacherSubjects
                    .Where(link => link.IsHeadOfDepartment)
                    .Select(link => link.Teacher.Email)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TeacherOptionDto>> GetTeacherOptionsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Teachers
            .AsNoTracking()
            .OrderBy(teacher => teacher.FullName)
            .Select(teacher => new TeacherOptionDto(
                teacher.TeacherId,
                teacher.FullName,
                teacher.Email))
            .ToListAsync(cancellationToken);
    }

    public async Task<SubjectEditDto?> GetSubjectForEditAsync(Guid subjectId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Subjects
            .AsNoTracking()
            .Where(subject => subject.SubjectId == subjectId)
            .Select(subject => new SubjectEditDto(
                subject.SubjectId,
                subject.SubjectCode,
                subject.SubjectName,
                subject.Description,
                subject.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<SubjectDeletePreviewDto?> GetSubjectDeletePreviewAsync(
        Guid subjectId,
        CancellationToken cancellationToken = default)
    {
        var subject = await _dbContext.Subjects
            .AsNoTracking()
            .Where(candidate => candidate.SubjectId == subjectId)
            .Select(candidate => new SubjectEditDto(
                candidate.SubjectId,
                candidate.SubjectCode,
                candidate.SubjectName,
                candidate.Description,
                candidate.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        if (subject is null)
        {
            return null;
        }

        var sessionIds = await _dbContext.Sessions
            .AsNoTracking()
            .Where(session => session.SubjectId == subjectId)
            .Select(session => session.SessionId)
            .ToListAsync(cancellationToken);

        var chapterIds = await _dbContext.Chapters
            .AsNoTracking()
            .Where(chapter => chapter.SubjectId == subjectId)
            .Select(chapter => chapter.ChapterId)
            .ToListAsync(cancellationToken);

        var documentIds = await _dbContext.Documents
            .AsNoTracking()
            .Where(document => document.SubjectId == subjectId)
            .Select(document => document.DocumentId)
            .ToListAsync(cancellationToken);

        var counts = new SubjectDeleteCountsDto(
            TeacherAssignments: await _dbContext.TeacherSubjects.CountAsync(item => item.SubjectId == subjectId, cancellationToken),
            Sessions: sessionIds.Count,
            Messages: sessionIds.Count == 0
                ? 0
                : await _dbContext.Messages.CountAsync(message => sessionIds.Contains(message.SessionId), cancellationToken),
            Chapters: chapterIds.Count,
            Documents: documentIds.Count,
            ProcessingJobs: documentIds.Count == 0
                ? 0
                : await _dbContext.ProcessingJobs.CountAsync(job => documentIds.Contains(job.DocumentId), cancellationToken),
            Chunks: documentIds.Count == 0
                ? 0
                : await _dbContext.Chunks.CountAsync(chunk => documentIds.Contains(chunk.DocumentId), cancellationToken),
            TestQuestions: chapterIds.Count == 0
                ? 0
                : await _dbContext.TestQuestions.CountAsync(question => chapterIds.Contains(question.ChapterId), cancellationToken));

        return new SubjectDeletePreviewDto(
            subject.SubjectId,
            subject.SubjectCode,
            subject.SubjectName,
            subject.Description,
            subject.CreatedAt,
            counts);
    }

    public async Task<(bool Succeeded, string Message, Guid? SubjectId)> CreateSubjectAsync(
        UpsertSubjectRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = request.SubjectCode.Trim();
        var normalizedName = request.SubjectName.Trim();
        var normalizedDescription = NormalizeDescription(request.Description);

        if (await SubjectCodeExistsAsync(normalizedCode, null, cancellationToken))
        {
            return (false, "A subject with this code already exists.", null);
        }

        var subject = new Subject
        {
            SubjectId = Guid.NewGuid(),
            SubjectCode = normalizedCode,
            SubjectName = normalizedName,
            Description = normalizedDescription,
            CreatedAt = CreateDatabaseTimestamp()
        };

        _dbContext.Subjects.Add(subject);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return (true, "Subject created successfully.", subject.SubjectId);
    }

    public async Task<OperationResult> UpdateSubjectAsync(
        Guid subjectId,
        UpsertSubjectRequest request,
        CancellationToken cancellationToken = default)
    {
        var subject = await _dbContext.Subjects.FirstOrDefaultAsync(
            candidate => candidate.SubjectId == subjectId,
            cancellationToken);

        if (subject is null)
        {
            return OperationResult.Failure("The selected subject no longer exists.");
        }

        var normalizedCode = request.SubjectCode.Trim();
        var normalizedName = request.SubjectName.Trim();
        var normalizedDescription = NormalizeDescription(request.Description);

        if (await SubjectCodeExistsAsync(normalizedCode, subjectId, cancellationToken))
        {
            return OperationResult.Failure("A subject with this code already exists.");
        }

        subject.SubjectCode = normalizedCode;
        subject.SubjectName = normalizedName;
        subject.Description = normalizedDescription;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return OperationResult.Success("Subject updated successfully.");
    }

    public async Task<OperationResult> AssignHeaderTeacherAsync(
        Guid subjectId,
        Guid teacherId,
        CancellationToken cancellationToken = default)
    {
        var subject = await _dbContext.Subjects
            .FirstOrDefaultAsync(candidate => candidate.SubjectId == subjectId, cancellationToken);
        if (subject is null)
        {
            return OperationResult.Failure("The selected subject no longer exists.");
        }

        var teacher = await _dbContext.Teachers
            .FirstOrDefaultAsync(candidate => candidate.TeacherId == teacherId, cancellationToken);
        if (teacher is null)
        {
            return OperationResult.Failure("The selected teacher no longer exists.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var subjectLinks = await _dbContext.TeacherSubjects
            .Where(link => link.SubjectId == subjectId)
            .ToListAsync(cancellationToken);

        foreach (var link in subjectLinks)
        {
            link.IsHeadOfDepartment = false;
        }

        var selectedLink = subjectLinks.FirstOrDefault(link => link.TeacherId == teacherId);
        if (selectedLink is null)
        {
            selectedLink = new TeacherSubject
            {
                TeacherSubjectId = Guid.NewGuid(),
                SubjectId = subjectId,
                TeacherId = teacherId,
                IsHeadOfDepartment = true,
                CreatedAt = CreateDatabaseTimestamp()
            };
            _dbContext.TeacherSubjects.Add(selectedLink);
        }
        else
        {
            selectedLink.IsHeadOfDepartment = true;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return OperationResult.Success("Header teacher assigned successfully.");
    }

    public async Task<OperationResult> DeleteSubjectAsync(Guid subjectId, CancellationToken cancellationToken = default)
    {
        var subject = await _dbContext.Subjects
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.SubjectId == subjectId, cancellationToken);

        if (subject is null)
        {
            return OperationResult.Failure("The selected subject no longer exists.");
        }

        var sessionIds = await _dbContext.Sessions
            .Where(session => session.SubjectId == subjectId)
            .Select(session => session.SessionId)
            .ToListAsync(cancellationToken);

        var chapterIds = await _dbContext.Chapters
            .Where(chapter => chapter.SubjectId == subjectId)
            .Select(chapter => chapter.ChapterId)
            .ToListAsync(cancellationToken);

        var documentIds = await _dbContext.Documents
            .Where(document => document.SubjectId == subjectId)
            .Select(document => document.DocumentId)
            .ToListAsync(cancellationToken);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (sessionIds.Count > 0)
        {
            await _dbContext.Messages
                .Where(message => sessionIds.Contains(message.SessionId))
                .ExecuteDeleteAsync(cancellationToken);

            await _dbContext.Sessions
                .Where(session => sessionIds.Contains(session.SessionId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (documentIds.Count > 0)
        {
            await _dbContext.ProcessingJobs
                .Where(job => documentIds.Contains(job.DocumentId))
                .ExecuteDeleteAsync(cancellationToken);

            await _dbContext.Chunks
                .Where(chunk => documentIds.Contains(chunk.DocumentId))
                .ExecuteDeleteAsync(cancellationToken);

            await _dbContext.Documents
                .Where(document => documentIds.Contains(document.DocumentId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (chapterIds.Count > 0)
        {
            await _dbContext.TestQuestions
                .Where(question => chapterIds.Contains(question.ChapterId))
                .ExecuteDeleteAsync(cancellationToken);

            await _dbContext.Chapters
                .Where(chapter => chapterIds.Contains(chapter.ChapterId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        await _dbContext.TeacherSubjects
            .Where(item => item.SubjectId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);

        await _dbContext.Subjects
            .Where(item => item.SubjectId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return OperationResult.Success("Subject deleted successfully.");
    }

    private async Task<bool> SubjectCodeExistsAsync(
        string subjectCode,
        Guid? excludeSubjectId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Subjects.AnyAsync(
            subject => subject.SubjectCode.ToLower() == subjectCode.ToLower()
                && (!excludeSubjectId.HasValue || subject.SubjectId != excludeSubjectId.Value),
            cancellationToken);
    }

    private static string? NormalizeDescription(string? description)
    {
        return string.IsNullOrWhiteSpace(description)
            ? null
            : description.Trim();
    }

    private static DateTime CreateDatabaseTimestamp()
    {
        return DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
    }
}
