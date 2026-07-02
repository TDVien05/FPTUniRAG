using System;
using System.Collections.Generic;

namespace FPTUniRAG.DataAccessLayer.Entities;

public partial class StudentTokenUsageCurrentWeek
{
    public Guid? UserId { get; set; }

    public string? FullName { get; set; }

    public string? Email { get; set; }

    public decimal? PromptTokensUsedThisWeek { get; set; }

    public decimal? CompletionTokensUsedThisWeek { get; set; }

    public decimal? TotalTokensUsedThisWeek { get; set; }

    public long? RequestsThisWeek { get; set; }
}
