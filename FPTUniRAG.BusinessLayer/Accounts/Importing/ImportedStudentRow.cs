namespace FPTUniRAG.BusinessLayer.Accounts.Importing;

public sealed record ImportedStudentRow(
    int RowNumber,
    string StudentCode,
    string FullName,
    string Email);
