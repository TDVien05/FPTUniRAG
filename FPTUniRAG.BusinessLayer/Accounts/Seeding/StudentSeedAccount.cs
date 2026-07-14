namespace FPTUniRAG.BusinessLayer.Accounts.Seeding;

public sealed record StudentSeedAccount(
    string Email,
    string Password,
    string DisplayName,
    string StudentCode);
