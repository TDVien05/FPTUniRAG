namespace FPTUniRAG.BusinessLayer.Accounts.Importing;

public interface IStudentImportJobTracker
{
    Guid CreateJob();

    void ReportRowProcessed(Guid jobId, int processedRows, int totalRows);

    void Complete(Guid jobId, ImportStudentsResult result);

    void Fail(Guid jobId, string errorMessage);

    ImportJobStatusDto? GetStatus(Guid jobId);
}
