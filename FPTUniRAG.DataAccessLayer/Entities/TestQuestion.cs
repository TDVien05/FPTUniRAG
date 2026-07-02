using System;
using System.Collections.Generic;

namespace FPTUniRAG.DataAccessLayer.Entities;

public partial class TestQuestion
{
    public Guid QuestionId { get; set; }

    public Guid ChapterId { get; set; }

    public string QuestionText { get; set; } = null!;

    public string? Difficulty { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<BenchmarkResult> BenchmarkResults { get; set; } = new List<BenchmarkResult>();

    public virtual Chapter Chapter { get; set; } = null!;
}
