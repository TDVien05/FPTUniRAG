using System;
using System.Collections.Generic;

namespace FPTUniRAG.DataAccessLayer.Entities;

public partial class StudentTokenUsageCurrentMonth
{
    public Guid? UserId { get; set; }

    public string? FullName { get; set; }

    public string? Email { get; set; }

    public decimal? PromptTokensUsedThisMonth { get; set; }

    public decimal? CompletionTokensUsedThisMonth { get; set; }

    public decimal? TotalTokensUsedThisMonth { get; set; }

    public long? RequestsThisMonth { get; set; }
}
