using System.Collections.Concurrent;

namespace FPTUniRAG.BusinessLayer.Accounts.Importing;

public sealed class StudentImportJobTracker : IStudentImportJobTracker
{
    private static readonly TimeSpan JobRetention = TimeSpan.FromMinutes(30);

    private sealed record JobEntry(ImportJobStatusDto Status, DateTime CreatedAtUtc);

    private readonly ConcurrentDictionary<Guid, JobEntry> _jobs = new();

    public Guid CreateJob()
    {
        EvictExpiredJobs();

        var jobId = Guid.NewGuid();
        _jobs[jobId] = new JobEntry(ImportJobStatusDto.Queued(jobId), DateTime.UtcNow);
        return jobId;
    }

    public void ReportRowProcessed(Guid jobId, int processedRows, int totalRows)
    {
        Update(jobId, status => status with
        {
            Stage = "importing",
            ProgressPercent = totalRows == 0 ? 100 : Math.Clamp(5 + processedRows * 90 / totalRows, 5, 95),
            ProcessedRows = processedRows,
            TotalRows = totalRows
        });
    }

    public void Complete(Guid jobId, ImportStudentsResult result)
    {
        Update(jobId, status => status with
        {
            Stage = "completed",
            ProgressPercent = 100,
            IsCompleted = true,
            Result = result
        });
    }

    public void Fail(Guid jobId, string errorMessage)
    {
        Update(jobId, status => status with
        {
            Stage = "failed",
            IsFailed = true,
            ErrorMessage = errorMessage
        });
    }

    public ImportJobStatusDto? GetStatus(Guid jobId)
    {
        return _jobs.TryGetValue(jobId, out var entry) ? entry.Status : null;
    }

    private void Update(Guid jobId, Func<ImportJobStatusDto, ImportJobStatusDto> updater)
    {
        _jobs.AddOrUpdate(
            jobId,
            _ => throw new InvalidOperationException($"Import job {jobId} was not found."),
            (_, entry) => entry with { Status = updater(entry.Status) });
    }

    private void EvictExpiredJobs()
    {
        var cutoff = DateTime.UtcNow - JobRetention;
        foreach (var (jobId, entry) in _jobs)
        {
            if (entry.CreatedAtUtc < cutoff)
            {
                _jobs.TryRemove(jobId, out _);
            }
        }
    }
}
