using System;
using System.Collections.Generic;

namespace FPTUniRAG.DataAccessLayer.Entities;

public sealed class ChatBenchmarkRun
{
    public Guid ChatBenchmarkRunId { get; set; }

    /// <summary>Shared by every model started in the same benchmark press.</summary>
    public Guid? BatchId { get; set; }

    public string ModelName { get; set; } = null!;

    public Guid? SubjectId { get; set; }

    public int PromptCount { get; set; }

    public int CompletedCount { get; set; }

    public int SuccessCount { get; set; }

    public string Status { get; set; } = "queued";

    public DateTime StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? ErrorMessage { get; set; }

    public Guid? ExecutedBy { get; set; }

    public Subject? Subject { get; set; }

    public ICollection<ChatBenchmarkResult> Results { get; set; } = new List<ChatBenchmarkResult>();
}
