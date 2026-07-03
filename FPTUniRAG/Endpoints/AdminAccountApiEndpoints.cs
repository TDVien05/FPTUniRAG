using FPTUniRAG.BusinessLayer.Accounts;
using Microsoft.AspNetCore.Authorization;

namespace FPTUniRAG.Endpoints;

public static class AdminAccountApiEndpoints
{
    public static IEndpointRouteBuilder MapAdminAccountApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/accounts")
            .RequireAuthorization(new AuthorizeAttribute { Roles = "admin" });

        group.MapGet("/", async (
            IAccountManagementService accountManagementService,
            CancellationToken cancellationToken) =>
        {
            var accounts = await accountManagementService.GetManagedAccountsAsync(cancellationToken);
            return Results.Ok(accounts);
        });

        group.MapGet("/summary", async (
            IAccountManagementService accountManagementService,
            CancellationToken cancellationToken) =>
        {
            var summary = await accountManagementService.GetAccountSummaryAsync(cancellationToken);
            return Results.Ok(summary);
        });

        group.MapPost("/{userId:guid}/block", async (
            Guid userId,
            IAccountManagementService accountManagementService,
            CancellationToken cancellationToken) =>
        {
            var result = await accountManagementService.SetAccountBlockedStatusAsync(userId, true, cancellationToken);
            return result.Succeeded
                ? Results.Ok(result)
                : Results.BadRequest(result);
        });

        group.MapPost("/{userId:guid}/reactivate", async (
            Guid userId,
            IAccountManagementService accountManagementService,
            CancellationToken cancellationToken) =>
        {
            var result = await accountManagementService.SetAccountBlockedStatusAsync(userId, false, cancellationToken);
            return result.Succeeded
                ? Results.Ok(result)
                : Results.BadRequest(result);
        });

        return endpoints;
    }
}
