namespace FPTUniRAG.BusinessLayer.Accounts;

public sealed record StudentSeedAccount(
    string Email,
    string Password,
    string DisplayName,
    string StudentCode);
