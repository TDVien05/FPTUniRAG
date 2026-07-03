namespace FPTUniRAG.BusinessLayer.Accounts;

public sealed record ManagedAccountDto(
    Guid UserId,
    string FullName,
    string Email,
    string Role,
    bool IsBlocked,
    string? StudentCode,
    DateTime? CreatedAt);
