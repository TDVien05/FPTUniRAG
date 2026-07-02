using System;
using System.Collections.Generic;

namespace FPTUniRAG.DataAccessLayer.Entities;

public partial class StudentActiveChatEntitlement
{
    public Guid? UserId { get; set; }

    public string? FullName { get; set; }

    public string? Email { get; set; }

    public Guid? PlanId { get; set; }

    public string? PlanCode { get; set; }

    public string? PlanName { get; set; }

    public long? DailyTokenLimit { get; set; }

    public long? WeeklyTokenLimit { get; set; }

    public long? MonthlyTokenLimit { get; set; }

    public bool? HasUnlimitedChat { get; set; }

    public bool? HasAdvancedModels { get; set; }

    public bool? HasPrioritySupport { get; set; }

    public bool? HasFileUpload { get; set; }

    public bool? HasHistoryExport { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public string? SubscriptionStatus { get; set; }
}
