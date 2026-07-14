using FPTUniRAG.BusinessLayer.Subjects;
using FPTUniRAG.BusinessLayer.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace FPTUniRAG.BusinessLayer.Services;

public sealed class SignalRTeacherHeaderSubjectNotifier : ITeacherHeaderSubjectNotifier
{
    private readonly IHubContext<TeacherHeaderSubjectHub> _hubContext;

    public SignalRTeacherHeaderSubjectNotifier(IHubContext<TeacherHeaderSubjectHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyHeaderSubjectsChangedAsync(string teacherEmail, CancellationToken cancellationToken = default)
    {
        return NotifyHeaderSubjectsChangedAsync([teacherEmail], cancellationToken);
    }

    public Task NotifyHeaderSubjectsChangedAsync(IEnumerable<string> teacherEmails, CancellationToken cancellationToken = default)
    {
        var groups = teacherEmails
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(TeacherHeaderSubjectHub.GetTeacherGroupName)
            .ToArray();

        return groups.Length == 0
            ? Task.CompletedTask
            : _hubContext.Clients.Groups(groups).SendCoreAsync("headerSubjectsChanged", [], cancellationToken);
    }
}
