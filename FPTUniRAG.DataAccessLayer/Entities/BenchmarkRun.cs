using System;
using System.Collections.Generic;

namespace FPTUniRAG.DataAccessLayer.Entities;

public partial class BenchmarkRun
{
    public Guid BenchmarkRunId { get; set; }

    public string? RunName { get; set; }

    public Guid? ExecutedBy { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public virtual ICollection<BenchmarkResult> BenchmarkResults { get; set; } = new List<BenchmarkResult>();

    public virtual User? ExecutedByNavigation { get; set; }
}
