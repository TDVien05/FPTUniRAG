namespace FPTUniRAG.BusinessLayer.Subjects;

public interface ITeacherHeaderSubjectNotifier
{
    Task NotifyHeaderSubjectsChangedAsync(string teacherEmail, CancellationToken cancellationToken = default);

    Task NotifyHeaderSubjectsChangedAsync(IEnumerable<string> teacherEmails, CancellationToken cancellationToken = default);
}
