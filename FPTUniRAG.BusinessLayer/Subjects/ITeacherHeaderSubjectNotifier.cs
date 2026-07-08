namespace FPTUniRAG.BusinessLayer.Subjects;

public interface ITeacherHeaderSubjectNotifier
{
    Task NotifyHeaderSubjectsChangedAsync(string teacherEmail, CancellationToken cancellationToken = default);
}
