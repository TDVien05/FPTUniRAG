using FPTUniRAG.DataAccessLayer.Entities;

namespace FPTUniRAG.DataAccessLayer.Repositories.Subjects;

public interface ISubjectRepository
{
    Task<IReadOnlyList<Subject>> GetSubjectsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TeacherSubject>> GetHeaderLinksAsync(string teacherEmail, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TeacherSubject>> GetAssignedLinksAsync(string teacherEmail, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Teacher>> GetTeachersAsync(CancellationToken cancellationToken = default);
    Task<Subject?> FindSubjectAsync(Guid subjectId, bool tracked = false, CancellationToken cancellationToken = default);
    Task<SubjectDeleteCountRecord?> GetDeleteCountsAsync(Guid subjectId, CancellationToken cancellationToken = default);
    Task<bool> SubjectCodeExistsAsync(string code, Guid? excludeId, CancellationToken cancellationToken = default);
    Task AddSubjectAsync(Subject subject, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> SaveSubjectAsync(Subject subject, CancellationToken cancellationToken = default);
    Task<SubjectAssignmentRecord> AssignHeaderTeacherAsync(Guid subjectId, Guid teacherId, DateTime createdAt, CancellationToken cancellationToken = default);
    Task<SubjectAssignmentRecord> AssignTeacherAsync(Guid subjectId, Guid teacherId, DateTime createdAt, CancellationToken cancellationToken = default);
    Task<SubjectDeleteRecord> DeleteSubjectAsync(Guid subjectId, CancellationToken cancellationToken = default);
}

public sealed record SubjectDeleteCountRecord(int TeacherAssignments, int Sessions, int Messages, int Chapters, int Documents, int ProcessingJobs, int Chunks, int TestQuestions);
public sealed record SubjectAssignmentRecord(bool SubjectExists, bool TeacherExists, bool AlreadyAssigned, string? TeacherEmail, IReadOnlyList<string> PreviousHeaderEmails);
public sealed record SubjectDeleteRecord(bool Found, IReadOnlyList<string> HeaderTeacherEmails);
