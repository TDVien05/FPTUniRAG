using System;
using System.Collections.Generic;

namespace FPTUniRAG.DataAccessLayer.Entities;

public partial class StudentSubscription
{
    public Guid StudentSubscriptionId { get; set; }

    public Guid UserId { get; set; }

    public Guid PlanId { get; set; }

    public string SubscriptionStatus { get; set; } = null!;

    public DateTime StartedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public DateTime PurchasedAt { get; set; }

    public DateTime? CanceledAt { get; set; }

    public string? StripeSubscriptionId { get; set; }

    public bool AutoRenew { get; set; }

    public Guid? GrantedBy { get; set; }

    public string? Notes { get; set; }

    public virtual User? GrantedByNavigation { get; set; }

    public virtual SubscriptionPlan Plan { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
