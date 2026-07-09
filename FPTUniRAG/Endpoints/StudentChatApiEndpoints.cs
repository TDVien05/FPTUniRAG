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

        return endpoints;
    }
}
