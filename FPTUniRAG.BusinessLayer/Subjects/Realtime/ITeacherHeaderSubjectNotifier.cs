namespace FPTUniRAG.BusinessLayer.Subjects.Realtime;

public interface ITeacherHeaderSubjectNotifier
{
    Task NotifyHeaderSubjectsChangedAsync(string teacherEmail, CancellationToken cancellationToken = default);

    Task NotifyHeaderSubjectsChangedAsync(IEnumerable<string> teacherEmails, CancellationToken cancellationToken = default);
}
