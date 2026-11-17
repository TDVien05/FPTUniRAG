namespace FPTUniRAG.DataAccessLayer.Repositories.Seeding;

public sealed record DemoChapterSeed(string Title, int Order);

public sealed record DemoSubjectSeed(
    string Code,
    string Name,
    string Description,
    string ChunkingStrategy,
    IReadOnlyList<DemoChapterSeed> Chapters);

public interface IDemoDataRepository
{
    /// <summary>
    /// Idempotently ensures a teacher profile, the given subjects (with chapters),
    /// and that the teacher is head teacher of each subject that has none.
    /// </summary>
    Task EnsureTeacherWithSubjectsAsync(
        string teacherEmail,
        string teacherFullName,
        string? department,
        IReadOnlyList<DemoSubjectSeed> subjects,
        CancellationToken cancellationToken = default);
}
