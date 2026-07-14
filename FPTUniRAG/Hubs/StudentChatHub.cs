using System.Security.Claims;
using FPTUniRAG.BusinessLayer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace FPTUniRAG.Hubs;

[Authorize(Policy = "StudentOrAdmin")]
public sealed class StudentChatHub : Hub
{
    private readonly IStudentChatService _studentChatService;
    private readonly ILogger<StudentChatHub> _logger;

    public StudentChatHub(
        IStudentChatService studentChatService,
        ILogger<StudentChatHub> logger)
    {
        _studentChatService = studentChatService;
        _logger = logger;
    }

    public async Task SendMessage(StudentChatSendRequest request)
    {
        var cancellationToken = Context.ConnectionAborted;

        var rawUserId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(rawUserId, out var userId))
        {
            await Clients.Caller.SendAsync(
                "chatEvent",
                "error",
                new StudentChatErrorDto("Your login session is invalid. Please sign in again."),
                cancellationToken);
            return;
        }

        try
        {
            async Task WriteEventAsync(string eventName, object payload, CancellationToken token)
            {
                await Clients.Caller.SendAsync("chatEvent", eventName, payload, token);
            }

            await _studentChatService.StreamMessageAsync(userId, request, WriteEventAsync, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Student chat request cancelled for connection {ConnectionId}", Context.ConnectionId);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unhandled student chat hub failure for connection {ConnectionId}", Context.ConnectionId);
            await Clients.Caller.SendAsync(
                "chatEvent",
                "error",
                new StudentChatErrorDto("Chat failed on the server. Please try again."),
                cancellationToken);
        }
    }
}
