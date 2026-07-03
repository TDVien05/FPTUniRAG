namespace FPTUniRAG.BusinessLayer.Accounts;

public sealed record AdminSeedAccount(
    string Email,
    string Password,
    string DisplayName);
