using FPTUniRAG.BusinessLayer.Common;
using FPTUniRAG.BusinessLayer.Subjects.Realtime;
using FPTUniRAG.DataAccessLayer.Entities;
using FPTUniRAG.DataAccessLayer.Repositories.Subjects;

namespace FPTUniRAG.BusinessLayer.Subjects;

public sealed class SubjectManagementService(ISubjectRepository repository, ITeacherHeaderSubjectNotifier notifier) : ISubjectManagementService
{
    public async Task<IReadOnlyList<SubjectListItemDto>> GetSubjectsAsync(CancellationToken cancellationToken = default) =>
        (await repository.GetSubjectsAsync(cancellationToken)).Select(s => new SubjectListItemDto(s.SubjectId, s.SubjectCode, s.SubjectName, s.Description, s.DefaultChunkingStrategy, s.DefaultFixedChunkSize, s.CreatedAt)).ToList();

    public async Task<IReadOnlyList<SubjectTeacherAssignmentListItemDto>> GetSubjectsForTeacherAssignmentAsync(CancellationToken cancellationToken = default) =>
        (await repository.GetSubjectsAsync(cancellationToken)).Select(s => new SubjectTeacherAssignmentListItemDto(s.SubjectId, s.SubjectCode, s.SubjectName,
            s.TeacherSubjects.OrderBy(l => l.Teacher.FullName).Select(l => new TeacherAssignmentDto(l.TeacherId, l.Teacher.FullName, l.Teacher.Email, l.IsHeadOfDepartment)).ToList())).ToList();

    public async Task<IReadOnlyList<TeacherHeaderSubjectDashboardItemDto>> GetHeaderSubjectsForTeacherAsync(string teacherEmail, CancellationToken cancellationToken = default) =>
        (await repository.GetHeaderLinksAsync(teacherEmail.Trim(), cancellationToken)).Select(l => new TeacherHeaderSubjectDashboardItemDto(l.SubjectId, l.Subject.SubjectCode, l.Subject.SubjectName,
            l.Subject.Description, l.Subject.DefaultChunkingStrategy, l.Subject.DefaultFixedChunkSize, l.Subject.Documents.Count, l.Subject.Chapters.Count)).ToList();

    public async Task<IReadOnlyList<TeacherDocumentManagementItemDto>> GetDocumentManagementItemsForTeacherAsync(string teacherEmail, CancellationToken cancellationToken = default) =>
        (await repository.GetHeaderLinksAsync(teacherEmail.Trim(), cancellationToken)).Select(l =>
        {
            var documents = l.Subject.Documents.OrderBy(d => d.Chapter.ChapterOrder).ThenBy(d => d.CreatedAt).ToList();
            var latest = documents.OrderByDescending(d => d.CreatedAt).FirstOrDefault();
            return new TeacherDocumentManagementItemDto(l.SubjectId, l.Subject.SubjectCode, l.Subject.SubjectName, documents.Count, latest?.CreatedAt, latest?.DocumentId,
                latest?.Title, latest?.Status, documents.Select(d =>
                {
                    var latestJob = d.ProcessingJobs.OrderByDescending(job => job.StartedAt ?? DateTime.MinValue).FirstOrDefault();
                    return new TeacherSubjectDocumentDto(d.DocumentId, d.ChapterId, d.Chapter.ChapterTitle, d.Title,
                        d.Status ?? "unknown", latestJob?.ErrorMessage, d.Chunks.Count, d.CreatedAt);
                }).ToList());
        }).ToList();

    public async Task<IReadOnlyList<SubjectHeaderAssignmentListItemDto>> GetSubjectsForHeaderAssignmentAsync(CancellationToken cancellationToken = default) =>
        (await repository.GetSubjectsAsync(cancellationToken)).Select(s =>
        {
            var header = s.TeacherSubjects.FirstOrDefault(l => l.IsHeadOfDepartment);
            return new SubjectHeaderAssignmentListItemDto(s.SubjectId, s.SubjectCode, s.SubjectName, header?.TeacherId, header?.Teacher.FullName, header?.Teacher.Email);
        }).ToList();

    public async Task<IReadOnlyList<TeacherOptionDto>> GetTeacherOptionsAsync(CancellationToken cancellationToken = default) =>
        (await repository.GetTeachersAsync(cancellationToken)).Select(t => new TeacherOptionDto(t.TeacherId, t.FullName, t.Email)).ToList();

    public async Task<SubjectEditDto?> GetSubjectForEditAsync(Guid subjectId, CancellationToken cancellationToken = default)
    {
        var s = await repository.FindSubjectAsync(subjectId, cancellationToken: cancellationToken);
        return s is null ? null : ToEdit(s);
    }

    public async Task<SubjectDeletePreviewDto?> GetSubjectDeletePreviewAsync(Guid subjectId, CancellationToken cancellationToken = default)
    {
        var subject = await repository.FindSubjectAsync(subjectId, cancellationToken: cancellationToken);
        var counts = await repository.GetDeleteCountsAsync(subjectId, cancellationToken);
        return subject is null || counts is null ? null : new SubjectDeletePreviewDto(subject.SubjectId, subject.SubjectCode, subject.SubjectName, subject.Description, subject.CreatedAt,
            new SubjectDeleteCountsDto(counts.TeacherAssignments, counts.Sessions, counts.Messages, counts.Chapters, counts.Documents, counts.ProcessingJobs, counts.Chunks, counts.TestQuestions));
    }

    public async Task<(bool Succeeded, string Message, Guid? SubjectId)> CreateSubjectAsync(UpsertSubjectRequest request, CancellationToken cancellationToken = default)
    {
        var validation = Validate(request); if (validation is not null) return (false, validation, null);
        var code = request.SubjectCode.Trim();
        if (await repository.SubjectCodeExistsAsync(code, null, cancellationToken)) return (false, "A subject with this code already exists.", null);
        var subject = new Subject { SubjectId = Guid.NewGuid(), SubjectCode = code, SubjectName = request.SubjectName.Trim(), Description = NormalizeDescription(request.Description),
            DefaultChunkingStrategy = NormalizeChunkingStrategy(request.DefaultChunkingStrategy), DefaultFixedChunkSize = request.DefaultFixedChunkSize, CreatedAt = DatabaseTimestamp() };
        await repository.AddSubjectAsync(subject, cancellationToken); return (true, "Subject created successfully.", subject.SubjectId);
    }

    public async Task<OperationResult> UpdateSubjectAsync(Guid subjectId, UpsertSubjectRequest request, CancellationToken cancellationToken = default)
    {
        var subject = await repository.FindSubjectAsync(subjectId, true, cancellationToken); if (subject is null) return OperationResult.Failure("The selected subject no longer exists.");
        var validation = Validate(request); if (validation is not null) return OperationResult.Failure(validation);
        var code = request.SubjectCode.Trim(); if (await repository.SubjectCodeExistsAsync(code, subjectId, cancellationToken)) return OperationResult.Failure("A subject with this code already exists.");
        subject.SubjectCode = code; subject.SubjectName = request.SubjectName.Trim(); subject.Description = NormalizeDescription(request.Description);
        subject.DefaultChunkingStrategy = NormalizeChunkingStrategy(request.DefaultChunkingStrategy); subject.DefaultFixedChunkSize = request.DefaultFixedChunkSize;
        var emails = await repository.SaveSubjectAsync(subject, cancellationToken); await notifier.NotifyHeaderSubjectsChangedAsync(emails, cancellationToken);
        return OperationResult.Success("Subject updated successfully.");
    }

    public async Task<OperationResult> AssignHeaderTeacherAsync(Guid subjectId, Guid teacherId, CancellationToken cancellationToken = default)
    {
        var result = await repository.AssignHeaderTeacherAsync(subjectId, teacherId, DatabaseTimestamp(), cancellationToken);
        if (!result.SubjectExists) return OperationResult.Failure("The selected subject no longer exists.");
        if (!result.TeacherExists) return OperationResult.Failure("The selected teacher no longer exists.");
        await NotifyAsync(result.PreviousHeaderEmails, result.TeacherEmail, cancellationToken);
        return OperationResult.Success("Header teacher assigned successfully.");
    }

    public async Task<OperationResult> AssignTeacherAsync(Guid subjectId, Guid teacherId, CancellationToken cancellationToken = default)
    {
        var result = await repository.AssignTeacherAsync(subjectId, teacherId, DatabaseTimestamp(), cancellationToken);
        if (!result.SubjectExists) return OperationResult.Failure("The selected subject no longer exists.");
        if (!result.TeacherExists) return OperationResult.Failure("The selected teacher no longer exists.");
        return OperationResult.Success(result.AlreadyAssigned ? "Teacher is already assigned to this subject." : "Teacher assigned to subject successfully.");
    }

    public async Task<OperationResult> DeleteSubjectAsync(Guid subjectId, CancellationToken cancellationToken = default)
    {
        var result = await repository.DeleteSubjectAsync(subjectId, cancellationToken);
        if (!result.Found) return OperationResult.Failure("The selected subject no longer exists.");
        await notifier.NotifyHeaderSubjectsChangedAsync(result.HeaderTeacherEmails, cancellationToken);
        return OperationResult.Success("Subject deleted successfully.");
    }

    private static SubjectEditDto ToEdit(Subject s) => new(s.SubjectId, s.SubjectCode, s.SubjectName, s.Description, s.DefaultChunkingStrategy, s.DefaultFixedChunkSize, s.CreatedAt);
    private static string? Validate(UpsertSubjectRequest r) => !SubjectChunkingStrategies.IsSupported(r.DefaultChunkingStrategy) ? "The selected chunking strategy is invalid." : r.DefaultFixedChunkSize <= 0 ? "Fixed chunk size must be greater than zero." : null;
    private static string? NormalizeDescription(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string NormalizeChunkingStrategy(string? value) => SubjectChunkingStrategies.Normalize(value);
    private static DateTime DatabaseTimestamp() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
    private async Task NotifyAsync(IEnumerable<string> previous, string? current, CancellationToken token)
    { foreach (var email in previous.Append(current ?? "").Where(e => !string.IsNullOrWhiteSpace(e)).Distinct(StringComparer.OrdinalIgnoreCase)) await notifier.NotifyHeaderSubjectsChangedAsync(email, token); }
}
