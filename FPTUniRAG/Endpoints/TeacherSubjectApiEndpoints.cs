using System.Security.Claims;
using FPTUniRAG.BusinessLayer.Subjects;

namespace FPTUniRAG.Endpoints;

public static class TeacherSubjectApiEndpoints
{
    public static IEndpointRouteBuilder MapTeacherSubjectApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/teacher/subjects")
            .RequireAuthorization("TeacherOrAdmin");

        group.MapGet("/header", async (
            HttpContext httpContext,
            ISubjectManagementService subjectManagementService,
            CancellationToken cancellationToken) =>
        {
            var teacherEmail = httpContext.User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(teacherEmail))
            {
                return Results.Unauthorized();
            }

            var subjects = await subjectManagementService.GetHeaderSubjectsForTeacherAsync(
                teacherEmail,
                cancellationToken);

            return Results.Ok(subjects);
        });

        return endpoints;
    }
}
