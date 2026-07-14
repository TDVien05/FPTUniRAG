using System.Security.Claims;
using FPTUniRAG.DataAccessLayer.Context;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FPTUniRAG.BusinessLayer.Services;

public sealed class AccountCookieAuthenticationEvents : CookieAuthenticationEvents
{
    private static readonly PathString ApiPathPrefix = new("/api");
    private static readonly PathString HubPathPrefix = new("/hubs");
    private readonly AppDbContext _dbContext;
    private readonly ILogger<AccountCookieAuthenticationEvents> _logger;

    public AccountCookieAuthenticationEvents(
        AppDbContext dbContext,
        ILogger<AccountCookieAuthenticationEvents> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        try
        {
            var emailValue = context.Principal?.FindFirstValue(ClaimTypes.Email);
            var accountState = await FindByEmailAsync(emailValue, context.HttpContext.RequestAborted);
            if (accountState is null || accountState.IsBlocked)
            {
                await RejectAsync(context);
                return;
            }

            RefreshPrincipal(context, accountState.UserId, accountState.Email, accountState.FullName, accountState.Role);
            await base.ValidatePrincipal(context);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to validate auth cookie for a protected request.");
            await RejectAsync(context);
        }
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (IsApiRequest(context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        if (context.HttpContext.User.Identity?.IsAuthenticated == true)
        {
            context.Response.Redirect(AccountNavigation.GetLandingPagePath(context.HttpContext.User));
            return Task.CompletedTask;
        }

        context.Response.Redirect(AccountNavigation.LoginPath);
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (IsApiRequest(context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        context.Response.Redirect(AccountNavigation.GetLandingPagePath(context.HttpContext.User));
        return Task.CompletedTask;
    }

    private async Task<CookieAccountState?> FindByEmailAsync(string? email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return await _dbContext.Users
            .AsNoTracking()
            .Where(candidate => candidate.Email == email)
            .Select(candidate => new CookieAccountState(
                candidate.UserId,
                candidate.Email,
                candidate.FullName ?? candidate.Email,
                candidate.Role ?? "student",
                candidate.IsBlocked))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static void RefreshPrincipal(
        CookieValidatePrincipalContext context,
        Guid userId,
        string email,
        string fullName,
        string role)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, fullName),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Role, role.Trim().ToLowerInvariant())
        };

        context.ReplacePrincipal(new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme)));
        context.ShouldRenew = true;
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private static bool IsApiRequest(PathString path)
    {
        return path.StartsWithSegments(ApiPathPrefix, StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments(HubPathPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CookieAccountState(
        Guid UserId,
        string Email,
        string FullName,
        string Role,
        bool IsBlocked);
}
