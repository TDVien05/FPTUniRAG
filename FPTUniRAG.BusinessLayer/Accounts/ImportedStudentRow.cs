namespace FPTUniRAG.BusinessLayer.Accounts;

public sealed record ImportedStudentRow(
    int RowNumber,
    string StudentCode,
    string FullName,
    string Email);
