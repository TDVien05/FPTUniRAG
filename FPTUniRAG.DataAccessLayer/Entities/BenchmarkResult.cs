using System;
using System.Collections.Generic;

namespace FPTUniRAG.DataAccessLayer.Entities;

public partial class BenchmarkResult
{
    public Guid ResultId { get; set; }

    public Guid BenchmarkRunId { get; set; }

    public Guid? QuestionId { get; set; }

    public decimal? Score { get; set; }

    public int? ResponseTimeMs { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual BenchmarkRun BenchmarkRun { get; set; } = null!;

    public virtual TestQuestion? Question { get; set; }
}
