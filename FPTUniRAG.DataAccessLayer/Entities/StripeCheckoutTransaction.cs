using System;

namespace FPTUniRAG.DataAccessLayer.Entities;

public partial class StripeCheckoutTransaction
{
    public Guid StripeCheckoutTransactionId { get; set; }

    public Guid UserId { get; set; }

    public Guid PlanId { get; set; }

    public string CheckoutId { get; set; } = null!;

    public string CheckoutUrl { get; set; } = null!;

    public string PaymentStatus { get; set; } = null!;

    public decimal Amount { get; set; }

    public string? StripePriceId { get; set; }

    public string? RawRequestJson { get; set; }

    public string? RawResponseJson { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ConfirmedAt { get; set; }

    public virtual SubscriptionPlan Plan { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
