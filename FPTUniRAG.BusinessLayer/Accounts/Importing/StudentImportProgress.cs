namespace FPTUniRAG.BusinessLayer.Accounts.Importing;

public readonly record struct StudentImportProgress(int ProcessedRows, int TotalRows);
