using System.Security.Claims;
using FPTUniRAG.DataAccessLayer.Repositories.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Logging;

namespace FPTUniRAG.BusinessLayer.Accounts.Authentication;

public sealed class AccountCookieAuthenticationEvents : CookieAuthenticationEvents
{
    private static readonly PathString ApiPathPrefix = new("/api");
    private static readonly PathString HubPathPrefix = new("/hubs");
    private readonly IAccountRepository _accountRepository;
    private readonly ILogger<AccountCookieAuthenticationEvents> _logger;

    public AccountCookieAuthenticationEvents(
        IAccountRepository accountRepository,
        ILogger<AccountCookieAuthenticationEvents> logger)
    {
        _accountRepository = accountRepository;
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

            RefreshPrincipal(
                context,
                accountState.UserId,
                accountState.Email,
                accountState.FullName,
                accountState.Role,
                accountState.MustChangePassword);
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

        var user = await _accountRepository.FindUserByEmailAsync(email, cancellationToken: cancellationToken);
        return user is null ? null : new CookieAccountState(
            user.UserId,
            user.Email,
            user.FullName ?? user.Email,
            user.Role ?? "student",
            user.IsBlocked,
            user.MustChangePassword);
    }

    private static void RefreshPrincipal(
        CookieValidatePrincipalContext context,
        Guid userId,
        string email,
        string fullName,
        string role,
        bool mustChangePassword)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, fullName),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Role, role.Trim().ToLowerInvariant()),
            new(AccountClaimTypes.MustChangePassword, mustChangePassword.ToString().ToLowerInvariant())
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
        bool IsBlocked,
        bool MustChangePassword);
}
