namespace FPTUniRAG.BusinessLayer.Accounts.Importing;

public sealed record ImportStudentsRowResult(
    int RowNumber,
    string StudentCode,
    string Email,
    bool IsCreated,
    string Message);
