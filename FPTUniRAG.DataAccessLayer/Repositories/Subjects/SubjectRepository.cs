using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;

namespace FPTUniRAG.DataAccessLayer.Repositories.Subjects;

public sealed class SubjectRepository(AppDbContext context) : ISubjectRepository
{
    public async Task<IReadOnlyList<Subject>> GetSubjectsAsync(CancellationToken cancellationToken = default) =>
        await context.Subjects.AsNoTracking().Include(s => s.TeacherSubjects).ThenInclude(l => l.Teacher)
            .Include(s => s.Chapters).Include(s => s.Documents).ThenInclude(d => d.Chapter)
            .Include(s => s.Documents).ThenInclude(d => d.Chunks).OrderBy(s => s.SubjectCode).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TeacherSubject>> GetHeaderLinksAsync(string teacherEmail, CancellationToken cancellationToken = default) =>
        await context.TeacherSubjects.AsNoTracking().Where(l => l.IsHeadOfDepartment && l.Teacher.Email != null && l.Teacher.Email.ToLower() == teacherEmail.ToLower())
            .Include(l => l.Teacher).Include(l => l.Subject).ThenInclude(s => s.Chapters)
            .Include(l => l.Subject).ThenInclude(s => s.Documents).ThenInclude(d => d.Chapter)
            .Include(l => l.Subject).ThenInclude(s => s.Documents).ThenInclude(d => d.Chunks)
            .OrderBy(l => l.Subject.SubjectCode).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Teacher>> GetTeachersAsync(CancellationToken cancellationToken = default) =>
        await context.Teachers.AsNoTracking().OrderBy(t => t.FullName).ToListAsync(cancellationToken);

    public Task<Subject?> FindSubjectAsync(Guid subjectId, bool tracked = false, CancellationToken cancellationToken = default)
    { var query = tracked ? context.Subjects : context.Subjects.AsNoTracking(); return query.FirstOrDefaultAsync(s => s.SubjectId == subjectId, cancellationToken); }

    public async Task<SubjectDeleteCountRecord?> GetDeleteCountsAsync(Guid subjectId, CancellationToken cancellationToken = default)
    {
        if (!await context.Subjects.AnyAsync(s => s.SubjectId == subjectId, cancellationToken)) return null;
        var sessionIds = await context.Sessions.Where(s => s.SubjectId == subjectId).Select(s => s.SessionId).ToListAsync(cancellationToken);
        var chapterIds = await context.Chapters.Where(c => c.SubjectId == subjectId).Select(c => c.ChapterId).ToListAsync(cancellationToken);
        var documentIds = await context.Documents.Where(d => d.SubjectId == subjectId).Select(d => d.DocumentId).ToListAsync(cancellationToken);
        return new(await context.TeacherSubjects.CountAsync(x => x.SubjectId == subjectId, cancellationToken), sessionIds.Count,
            await context.Messages.CountAsync(m => sessionIds.Contains(m.SessionId), cancellationToken), chapterIds.Count, documentIds.Count,
            await context.ProcessingJobs.CountAsync(j => documentIds.Contains(j.DocumentId), cancellationToken),
            await context.Chunks.CountAsync(c => documentIds.Contains(c.DocumentId), cancellationToken),
            await context.TestQuestions.CountAsync(q => chapterIds.Contains(q.ChapterId), cancellationToken));
    }

    public Task<bool> SubjectCodeExistsAsync(string code, Guid? excludeId, CancellationToken cancellationToken = default) =>
        context.Subjects.AnyAsync(s => s.SubjectCode.ToLower() == code.ToLower() && (!excludeId.HasValue || s.SubjectId != excludeId), cancellationToken);
    public async Task AddSubjectAsync(Subject subject, CancellationToken cancellationToken = default) { context.Subjects.Add(subject); await context.SaveChangesAsync(cancellationToken); }
    public async Task<IReadOnlyList<string>> SaveSubjectAsync(Subject subject, CancellationToken cancellationToken = default)
    { var emails = await HeaderEmails(subject.SubjectId, cancellationToken); await context.SaveChangesAsync(cancellationToken); return emails; }

    public async Task<SubjectAssignmentRecord> AssignHeaderTeacherAsync(Guid subjectId, Guid teacherId, DateTime createdAt, CancellationToken cancellationToken = default)
    {
        if (!await context.Subjects.AnyAsync(s => s.SubjectId == subjectId, cancellationToken)) return new(false, true, false, null, []);
        var teacher = await context.Teachers.FirstOrDefaultAsync(t => t.TeacherId == teacherId, cancellationToken);
        if (teacher is null) return new(true, false, false, null, []);
        var previous = await HeaderEmails(subjectId, cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var links = await context.TeacherSubjects.Where(l => l.SubjectId == subjectId).ToListAsync(cancellationToken);
        foreach (var link in links) link.IsHeadOfDepartment = false;
        var selected = links.FirstOrDefault(l => l.TeacherId == teacherId);
        if (selected is null) context.TeacherSubjects.Add(new TeacherSubject { TeacherSubjectId = Guid.NewGuid(), SubjectId = subjectId, TeacherId = teacherId, IsHeadOfDepartment = true, CreatedAt = createdAt });
        else selected.IsHeadOfDepartment = true;
        await context.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return new(true, true, false, teacher.Email, previous);
    }

    public async Task<SubjectAssignmentRecord> AssignTeacherAsync(Guid subjectId, Guid teacherId, DateTime createdAt, CancellationToken cancellationToken = default)
    {
        if (!await context.Subjects.AnyAsync(s => s.SubjectId == subjectId, cancellationToken)) return new(false, true, false, null, []);
        if (!await context.Teachers.AnyAsync(t => t.TeacherId == teacherId, cancellationToken)) return new(true, false, false, null, []);
        if (await context.TeacherSubjects.AnyAsync(l => l.SubjectId == subjectId && l.TeacherId == teacherId, cancellationToken)) return new(true, true, true, null, []);
        context.TeacherSubjects.Add(new TeacherSubject { TeacherSubjectId = Guid.NewGuid(), SubjectId = subjectId, TeacherId = teacherId, IsHeadOfDepartment = false, CreatedAt = createdAt });
        await context.SaveChangesAsync(cancellationToken); return new(true, true, false, null, []);
    }

    public async Task<SubjectDeleteRecord> DeleteSubjectAsync(Guid subjectId, CancellationToken cancellationToken = default)
    {
        if (!await context.Subjects.AnyAsync(s => s.SubjectId == subjectId, cancellationToken)) return new(false, []);
        var emails = await HeaderEmails(subjectId, cancellationToken);
        var sessionIds = await context.Sessions.Where(s => s.SubjectId == subjectId).Select(s => s.SessionId).ToListAsync(cancellationToken);
        var chapterIds = await context.Chapters.Where(c => c.SubjectId == subjectId).Select(c => c.ChapterId).ToListAsync(cancellationToken);
        var documentIds = await context.Documents.Where(d => d.SubjectId == subjectId).Select(d => d.DocumentId).ToListAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Messages.Where(m => sessionIds.Contains(m.SessionId)).ExecuteDeleteAsync(cancellationToken);
        await context.Sessions.Where(s => sessionIds.Contains(s.SessionId)).ExecuteDeleteAsync(cancellationToken);
        await context.ProcessingJobs.Where(j => documentIds.Contains(j.DocumentId)).ExecuteDeleteAsync(cancellationToken);
        await context.Chunks.Where(c => documentIds.Contains(c.DocumentId)).ExecuteDeleteAsync(cancellationToken);
        await context.Documents.Where(d => documentIds.Contains(d.DocumentId)).ExecuteDeleteAsync(cancellationToken);
        await context.TestQuestions.Where(q => chapterIds.Contains(q.ChapterId)).ExecuteDeleteAsync(cancellationToken);
        await context.Chapters.Where(c => chapterIds.Contains(c.ChapterId)).ExecuteDeleteAsync(cancellationToken);
        await context.TeacherSubjects.Where(l => l.SubjectId == subjectId).ExecuteDeleteAsync(cancellationToken);
        await context.Subjects.Where(s => s.SubjectId == subjectId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken); return new(true, emails);
    }

    private async Task<IReadOnlyList<string>> HeaderEmails(Guid subjectId, CancellationToken token) =>
        await context.TeacherSubjects.AsNoTracking().Where(l => l.SubjectId == subjectId && l.IsHeadOfDepartment && l.Teacher.Email != null).Select(l => l.Teacher.Email!).ToListAsync(token);
}
