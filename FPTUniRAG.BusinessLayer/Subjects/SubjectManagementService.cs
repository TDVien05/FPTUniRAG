using FPTUniRAG.BusinessLayer.Accounts;
using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;

namespace FPTUniRAG.BusinessLayer.Subjects;

public sealed class SubjectManagementService : ISubjectManagementService
{
    private readonly AppDbContext _dbContext;
    private readonly ITeacherHeaderSubjectNotifier _teacherHeaderSubjectNotifier;

    public SubjectManagementService(
        AppDbContext dbContext,
        ITeacherHeaderSubjectNotifier teacherHeaderSubjectNotifier)
    {
        _dbContext = dbContext;
        _teacherHeaderSubjectNotifier = teacherHeaderSubjectNotifier;
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
                subject.DefaultChunkingStrategy,
                subject.DefaultFixedChunkSize,
                subject.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SubjectTeacherAssignmentListItemDto>> GetSubjectsForTeacherAssignmentAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Subjects
            .AsNoTracking()
            .OrderBy(subject => subject.SubjectCode)
            .Select(subject => new SubjectTeacherAssignmentListItemDto(
                subject.SubjectId,
                subject.SubjectCode,
                subject.SubjectName,
                subject.TeacherSubjects
                    .OrderBy(link => link.Teacher.FullName)
                    .Select(link => new TeacherAssignmentDto(
                        link.TeacherId,
                        link.Teacher.FullName,
                        link.Teacher.Email,
                        link.IsHeadOfDepartment))
                    .ToList()))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TeacherHeaderSubjectDashboardItemDto>> GetHeaderSubjectsForTeacherAsync(
        string teacherEmail,
        CancellationToken cancellationToken = default)
    {
        var normalizedTeacherEmail = teacherEmail.Trim();

        return await _dbContext.TeacherSubjects
            .AsNoTracking()
            .Where(link =>
                link.IsHeadOfDepartment
                && link.Teacher.Email != null
                && link.Teacher.Email.ToLower() == normalizedTeacherEmail.ToLower())
            .OrderBy(link => link.Subject.SubjectCode)
            .Select(link => new TeacherHeaderSubjectDashboardItemDto(
                link.SubjectId,
                link.Subject.SubjectCode,
                link.Subject.SubjectName,
                link.Subject.Description,
                link.Subject.DefaultChunkingStrategy,
                link.Subject.DefaultFixedChunkSize,
                link.Subject.Documents.Count(),
                link.Subject.Chapters.Count()))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TeacherDocumentManagementItemDto>> GetDocumentManagementItemsForTeacherAsync(
        string teacherEmail,
        CancellationToken cancellationToken = default)
    {
        var normalizedTeacherEmail = teacherEmail.Trim();

        return await _dbContext.TeacherSubjects
            .AsNoTracking()
            .Where(link =>
                link.IsHeadOfDepartment
                && link.Teacher.Email != null
                && link.Teacher.Email.ToLower() == normalizedTeacherEmail.ToLower())
            .OrderBy(link => link.Subject.SubjectCode)
            .Select(link => new TeacherDocumentManagementItemDto(
                link.SubjectId,
                link.Subject.SubjectCode,
                link.Subject.SubjectName,
                link.Subject.Documents.Count(),
                link.Subject.Documents
                    .OrderByDescending(document => document.CreatedAt)
                    .Select(document => document.CreatedAt)
                    .FirstOrDefault(),
                link.Subject.Documents
                    .OrderByDescending(document => document.CreatedAt)
                    .Select(document => (Guid?)document.DocumentId)
                    .FirstOrDefault(),
                link.Subject.Documents
                    .OrderByDescending(document => document.CreatedAt)
                    .Select(document => document.Title)
                    .FirstOrDefault(),
                link.Subject.Documents
                    .OrderByDescending(document => document.CreatedAt)
                    .Select(document => document.Status)
                    .FirstOrDefault(),
                link.Subject.Documents
                    .OrderBy(document => document.Chapter.ChapterOrder)
                    .ThenBy(document => document.CreatedAt)
                    .Select(document => new TeacherSubjectDocumentDto(
                        document.DocumentId,
                        document.ChapterId,
                        document.Chapter.ChapterTitle,
                        document.Title,
                        document.Status ?? "unknown",
                        document.Chunks.Count(),
                        document.CreatedAt))
                    .ToList()))
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
                subject.DefaultChunkingStrategy,
                subject.DefaultFixedChunkSize,
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
                candidate.DefaultChunkingStrategy,
                candidate.DefaultFixedChunkSize,
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
        if (!SubjectChunkingStrategies.IsSupported(request.DefaultChunkingStrategy))
        {
            return (false, "The selected chunking strategy is invalid.", null);
        }

        if (request.DefaultFixedChunkSize <= 0)
        {
            return (false, "Fixed chunk size must be greater than zero.", null);
        }

        var normalizedChunkingStrategy = NormalizeChunkingStrategy(request.DefaultChunkingStrategy);

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
            DefaultChunkingStrategy = normalizedChunkingStrategy,
            DefaultFixedChunkSize = request.DefaultFixedChunkSize,
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
        if (!SubjectChunkingStrategies.IsSupported(request.DefaultChunkingStrategy))
        {
            return OperationResult.Failure("The selected chunking strategy is invalid.");
        }

        if (request.DefaultFixedChunkSize <= 0)
        {
            return OperationResult.Failure("Fixed chunk size must be greater than zero.");
        }

        var normalizedChunkingStrategy = NormalizeChunkingStrategy(request.DefaultChunkingStrategy);

        if (await SubjectCodeExistsAsync(normalizedCode, subjectId, cancellationToken))
        {
            return OperationResult.Failure("A subject with this code already exists.");
        }

        subject.SubjectCode = normalizedCode;
        subject.SubjectName = normalizedName;
        subject.Description = normalizedDescription;
        subject.DefaultChunkingStrategy = normalizedChunkingStrategy;
        subject.DefaultFixedChunkSize = request.DefaultFixedChunkSize;

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

        var previousHeaderTeacherEmails = await _dbContext.TeacherSubjects
            .Where(link => link.SubjectId == subjectId && link.IsHeadOfDepartment && link.Teacher.Email != null)
            .Select(link => link.Teacher.Email!)
            .ToListAsync(cancellationToken);

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
        await NotifyHeaderSubjectTeachersAsync(previousHeaderTeacherEmails, teacher.Email, cancellationToken);

        return OperationResult.Success("Header teacher assigned successfully.");
    }

    public async Task<OperationResult> AssignTeacherAsync(
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

        var existingLink = await _dbContext.TeacherSubjects
            .FirstOrDefaultAsync(
                link => link.SubjectId == subjectId && link.TeacherId == teacherId,
                cancellationToken);

        if (existingLink is not null)
        {
            return OperationResult.Success("Teacher is already assigned to this subject.");
        }

        _dbContext.TeacherSubjects.Add(new TeacherSubject
        {
            TeacherSubjectId = Guid.NewGuid(),
            SubjectId = subjectId,
            TeacherId = teacherId,
            IsHeadOfDepartment = false,
            CreatedAt = CreateDatabaseTimestamp()
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return OperationResult.Success("Teacher assigned to subject successfully.");
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

    private static string NormalizeChunkingStrategy(string? chunkingStrategy)
    {
        var normalized = SubjectChunkingStrategies.Normalize(chunkingStrategy);
        if (!SubjectChunkingStrategies.IsSupported(normalized))
        {
            throw new InvalidOperationException($"Unsupported subject chunking strategy '{chunkingStrategy}'.");
        }

        return normalized;
    }

    private static DateTime CreateDatabaseTimestamp()
    {
        return DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
    }

    private async Task NotifyHeaderSubjectTeachersAsync(
        IEnumerable<string> previousHeaderTeacherEmails,
        string? currentHeaderTeacherEmail,
        CancellationToken cancellationToken)
    {
        var emailsToNotify = previousHeaderTeacherEmails
            .Append(currentHeaderTeacherEmail ?? string.Empty)
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var email in emailsToNotify)
        {
            await _teacherHeaderSubjectNotifier.NotifyHeaderSubjectsChangedAsync(email, cancellationToken);
        }
    }
}
