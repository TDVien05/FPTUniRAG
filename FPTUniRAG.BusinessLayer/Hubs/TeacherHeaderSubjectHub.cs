using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FPTUniRAG.BusinessLayer.Hubs;

[Authorize(Policy = "TeacherOrAdmin")]
public sealed class TeacherHeaderSubjectHub : Hub
{
    internal static string GetTeacherGroupName(string teacherEmail)
    {
        return $"teacher-header-subjects:{teacherEmail.Trim().ToLowerInvariant()}";
    }

    public override async Task OnConnectedAsync()
    {
        var email = Context.User?.FindFirstValue(ClaimTypes.Email);
        if (!string.IsNullOrWhiteSpace(email))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GetTeacherGroupName(email));
        }

        await base.OnConnectedAsync();
    }
}
