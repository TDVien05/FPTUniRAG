using System;

namespace FPTUniRAG.DataAccessLayer.Entities;

public partial class MomoPaymentTransaction
{
    public Guid MomoPaymentTransactionId { get; set; }

    public Guid UserId { get; set; }

    public Guid PlanId { get; set; }

    public string OrderId { get; set; } = null!;

    public string RequestId { get; set; } = null!;

    public decimal Amount { get; set; }

    public string PaymentStatus { get; set; } = null!;

    public string? PayUrl { get; set; }

    public long? ResultCode { get; set; }

    public string? ProviderMessage { get; set; }

    public long? ProviderTransactionId { get; set; }

    public string? RawRequestJson { get; set; }

    public string? RawResponseJson { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ConfirmedAt { get; set; }

    public virtual SubscriptionPlan Plan { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
