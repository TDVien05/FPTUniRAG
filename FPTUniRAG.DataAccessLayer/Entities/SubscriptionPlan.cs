using System;
using System.Collections.Generic;

namespace FPTUniRAG.DataAccessLayer.Entities;

public partial class SubscriptionPlan
{
    public Guid PlanId { get; set; }

    public string PlanCode { get; set; } = null!;

    public string PlanName { get; set; } = null!;

    public string? Description { get; set; }

    public decimal MonthlyPrice { get; set; }

    public long? DailyTokenLimit { get; set; }

    public long? WeeklyTokenLimit { get; set; }

    public long? MonthlyTokenLimit { get; set; }

    public bool HasUnlimitedChat { get; set; }

    public bool HasAdvancedModels { get; set; }

    public bool HasPrioritySupport { get; set; }

    public bool HasFileUpload { get; set; }

    public bool HasHistoryExport { get; set; }

    public bool IsActive { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<StudentSubscription> StudentSubscriptions { get; set; } = new List<StudentSubscription>();

    public virtual ICollection<TokenUsageLog> TokenUsageLogs { get; set; } = new List<TokenUsageLog>();
}
