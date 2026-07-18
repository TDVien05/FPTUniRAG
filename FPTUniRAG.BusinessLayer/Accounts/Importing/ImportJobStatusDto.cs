namespace FPTUniRAG.BusinessLayer.Accounts.Importing;

public sealed record ImportJobStatusDto(
    Guid JobId,
    string Stage,
    int ProgressPercent,
    int ProcessedRows,
    int TotalRows,
    bool IsCompleted,
    bool IsFailed,
    string? ErrorMessage,
    ImportStudentsResult? Result)
{
    public static ImportJobStatusDto Queued(Guid jobId) => new(
        jobId,
        Stage: "queued",
        ProgressPercent: 0,
        ProcessedRows: 0,
        TotalRows: 0,
        IsCompleted: false,
        IsFailed: false,
        ErrorMessage: null,
        Result: null);
}
