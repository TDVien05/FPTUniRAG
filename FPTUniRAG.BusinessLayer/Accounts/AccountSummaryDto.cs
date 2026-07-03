namespace FPTUniRAG.BusinessLayer.Accounts;

public sealed record AccountSummaryDto(
    int TotalUsers,
    int StudentCount,
    int TeacherCount,
    int AdminCount,
    int BlockedCount);
