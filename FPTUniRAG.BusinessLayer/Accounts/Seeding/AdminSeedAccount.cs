namespace FPTUniRAG.BusinessLayer.Accounts.Seeding;

public sealed record AdminSeedAccount(
    string Email,
    string Password,
    string DisplayName);
