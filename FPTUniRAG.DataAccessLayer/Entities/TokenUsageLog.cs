using System;
using System.Collections.Generic;

namespace FPTUniRAG.DataAccessLayer.Entities;

public partial class TokenUsageLog
{
    public Guid TokenUsageId { get; set; }

    public Guid UserId { get; set; }

    public Guid? SessionId { get; set; }

    public Guid? MessageId { get; set; }

    public Guid? PlanId { get; set; }

    public string FeatureName { get; set; } = null!;

    public string? ProviderName { get; set; }

    public string? ModelName { get; set; }

    public long PromptTokens { get; set; }

    public long CompletionTokens { get; set; }

    public long TotalTokens { get; set; }

    public int RequestCount { get; set; }

    public int? ResponseTimeMs { get; set; }

    public DateTime UsedAt { get; set; }

    public string? MetadataJson { get; set; }

    public virtual Message? Message { get; set; }

    public virtual SubscriptionPlan? Plan { get; set; }

    public virtual Session? Session { get; set; }

    public virtual User User { get; set; } = null!;
}
