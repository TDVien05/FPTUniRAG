using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;

namespace FPTUniRAG.DataAccessLayer.Repositories.Seeding;

public sealed class DemoDataRepository(AppDbContext context) : IDemoDataRepository
{
    public async Task EnsureTeacherWithSubjectsAsync(
        string teacherEmail,
        string teacherFullName,
        string? department,
        IReadOnlyList<DemoSubjectSeed> subjects,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var email = teacherEmail.Trim();

        var teacher = await context.Teachers
            .FirstOrDefaultAsync(t => t.Email != null && t.Email.ToLower() == email.ToLower(), cancellationToken);
        if (teacher is null)
        {
            teacher = new Teacher { TeacherId = Guid.NewGuid(), CreatedAt = now };
            context.Teachers.Add(teacher);
        }
        teacher.FullName = teacherFullName;
        teacher.Email = email;
        teacher.Department = department;
        await context.SaveChangesAsync(cancellationToken);

        foreach (var seed in subjects)
        {
            var subject = await context.Subjects
                .FirstOrDefaultAsync(s => s.SubjectCode.ToLower() == seed.Code.ToLower(), cancellationToken);
            if (subject is null)
            {
                subject = new Subject
                {
                    SubjectId = Guid.NewGuid(),
                    SubjectCode = seed.Code,
                    SubjectName = seed.Name,
                    Description = seed.Description,
                    DefaultChunkingStrategy = seed.ChunkingStrategy,
                    CreatedAt = now
                };
                context.Subjects.Add(subject);
                await context.SaveChangesAsync(cancellationToken);
            }

            var existingTitles = (await context.Chapters
                    .Where(c => c.SubjectId == subject.SubjectId)
                    .Select(c => c.ChapterTitle)
                    .ToListAsync(cancellationToken))
                .Select(t => t.Trim().ToLowerInvariant())
                .ToHashSet();
            foreach (var chapter in seed.Chapters)
            {
                if (existingTitles.Contains(chapter.Title.Trim().ToLowerInvariant())) continue;
                context.Chapters.Add(new Chapter
                {
                    ChapterId = Guid.NewGuid(),
                    SubjectId = subject.SubjectId,
                    ChapterTitle = chapter.Title,
                    ChapterOrder = chapter.Order,
                    CreatedAt = now
                });
            }

            var hasLeader = await context.TeacherSubjects
                .AnyAsync(ts => ts.SubjectId == subject.SubjectId && ts.IsHeadOfDepartment, cancellationToken);
            var linked = await context.TeacherSubjects
                .AnyAsync(ts => ts.SubjectId == subject.SubjectId && ts.TeacherId == teacher.TeacherId, cancellationToken);
            if (!linked)
            {
                context.TeacherSubjects.Add(new TeacherSubject
                {
                    TeacherSubjectId = Guid.NewGuid(),
                    TeacherId = teacher.TeacherId,
                    SubjectId = subject.SubjectId,
                    // Only one head teacher per subject is allowed; never replace an existing one.
                    IsHeadOfDepartment = !hasLeader,
                    CreatedAt = now
                });
            }

            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
