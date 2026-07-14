namespace FPTUniRAG.BusinessLayer.Accounts.Authentication;

public sealed record AuthenticatedUser(
    Guid UserId,
    string Email,
    string FullName,
    string Role);
