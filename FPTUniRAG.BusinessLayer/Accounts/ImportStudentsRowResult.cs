namespace FPTUniRAG.BusinessLayer.Accounts;

public sealed record ImportStudentsRowResult(
    int RowNumber,
    string StudentCode,
    string Email,
    bool IsCreated,
    string Message);
