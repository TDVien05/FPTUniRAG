using System.Security.Claims;
using FPTUniRAG.Services;

namespace FPTUniRAG.Endpoints;

public static class StudentChatApiEndpoints
{
    public static IEndpointRouteBuilder MapStudentChatApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/student/chat")
            .RequireAuthorization("StudentOrAdmin");

        group.MapGet("/subjects", async (
            string? q,
            IStudentChatService studentChatService,
            CancellationToken cancellationToken) =>
        {
            var subjects = await studentChatService.SearchSubjectsAsync(q, cancellationToken);
            return Results.Ok(new StudentChatSubjectSearchResponseDto(subjects));
        });

        group.MapGet("/sessions", async (
            Guid? subjectId,
            HttpContext httpContext,
            IStudentChatService studentChatService,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(httpContext.User, out var userId))
            {
                return Results.Unauthorized();
            }

            var sessions = await studentChatService.GetSessionsAsync(userId, subjectId, cancellationToken);
            return Results.Ok(new StudentChatSessionListResponseDto(sessions));
        });

        group.MapGet("/sessions/{sessionId:guid}", async (
            Guid sessionId,
            HttpContext httpContext,
            IStudentChatService studentChatService,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(httpContext.User, out var userId))
            {
                return Results.Unauthorized();
            }

            var session = await studentChatService.GetSessionDetailAsync(userId, sessionId, cancellationToken);
            return session is null
                ? Results.NotFound()
                : Results.Ok(new StudentChatSessionDetailResponseDto(session));
        });

        group.MapGet("/sessions/{sessionId:guid}/citations", async (
            Guid sessionId,
            Guid documentId,
            int chunkIndex,
            HttpContext httpContext,
            IStudentChatService studentChatService,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetUserId(httpContext.User, out var userId))
            {
                return Results.Unauthorized();
            }

            var citation = await studentChatService.GetCitationDetailAsync(
                userId,
                sessionId,
                documentId,
                chunkIndex,
                cancellationToken);

            return citation is null
                ? Results.NotFound()
                : Results.Ok(new StudentChatCitationDetailResponseDto(citation));
        });

        return endpoints;
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        var rawUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(rawUserId, out userId);
    }
}
