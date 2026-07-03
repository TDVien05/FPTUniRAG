namespace FPTUniRAG.BusinessLayer.Accounts;

public sealed record AuthenticatedUser(
    Guid UserId,
    string Email,
    string FullName,
    string Role);
