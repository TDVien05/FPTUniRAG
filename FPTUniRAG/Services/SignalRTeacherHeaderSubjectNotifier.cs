using FPTUniRAG.BusinessLayer.Subjects;
using FPTUniRAG.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace FPTUniRAG.Services;

public sealed class SignalRTeacherHeaderSubjectNotifier : ITeacherHeaderSubjectNotifier
{
    private readonly IHubContext<TeacherHeaderSubjectHub> _hubContext;

    public SignalRTeacherHeaderSubjectNotifier(IHubContext<TeacherHeaderSubjectHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyHeaderSubjectsChangedAsync(string teacherEmail, CancellationToken cancellationToken = default)
    {
        return _hubContext.Clients
            .Group(TeacherHeaderSubjectHub.GetTeacherGroupName(teacherEmail))
            .SendCoreAsync("headerSubjectsChanged", [], cancellationToken);
    }
}
