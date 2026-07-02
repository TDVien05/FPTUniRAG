using System;
using System.Collections.Generic;

namespace FPTUniRAG.DataAccessLayer.Entities;

public partial class StudentTokenUsageCurrentDay
{
    public Guid? UserId { get; set; }

    public string? FullName { get; set; }

    public string? Email { get; set; }

    public decimal? PromptTokensUsedToday { get; set; }

    public decimal? CompletionTokensUsedToday { get; set; }

    public decimal? TotalTokensUsedToday { get; set; }

    public long? RequestsToday { get; set; }
}
