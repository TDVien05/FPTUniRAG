using System;

namespace FPTUniRAG.DataAccessLayer.Entities;

public sealed class ChatBenchmarkResult
{
    public Guid ResultId { get; set; }

    public Guid ChatBenchmarkRunId { get; set; }

    public string PromptText { get; set; } = null!;

    public string? AnswerText { get; set; }

    public int RetrievedChunkCount { get; set; }

    public long PromptTokens { get; set; }

    public long CompletionTokens { get; set; }

    public long TotalTokens { get; set; }

    public int? ResponseTimeMs { get; set; }

    public bool IsSuccess { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; }

    public ChatBenchmarkRun Run { get; set; } = null!;
}
