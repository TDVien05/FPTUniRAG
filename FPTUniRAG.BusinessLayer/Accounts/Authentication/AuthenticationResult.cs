namespace FPTUniRAG.BusinessLayer.Accounts.Authentication;

public enum AuthenticationStatus
{
    Success,
    InvalidCredentials,
    Blocked
}

public sealed record AuthenticationResult(
    AuthenticationStatus Status,
    AuthenticatedUser? User = null);
