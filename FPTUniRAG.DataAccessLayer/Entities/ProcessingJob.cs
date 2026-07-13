using System;
using System.Collections.Generic;

namespace FPTUniRAG.DataAccessLayer.Entities;

public partial class ProcessingJob
{
    public Guid JobId { get; set; }

    public Guid DocumentId { get; set; }

    public string? JobStatus { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public string? ErrorMessage { get; set; }

    public int ProgressPercent { get; set; }

    public string? ProcessingStage { get; set; }

    public virtual Document Document { get; set; } = null!;
}
