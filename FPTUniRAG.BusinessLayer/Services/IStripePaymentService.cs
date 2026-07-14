namespace FPTUniRAG.BusinessLayer.Services;

public interface IStripePaymentService
{
    Task<StripePlanPriceProvisionResult> EnsurePlanPriceAsync(
        Guid planId,
        string planCode,
        string planName,
        string? description,
        decimal monthlyPrice,
        string? existingStripePriceId,
        CancellationToken cancellationToken = default);

    Task<StripeCreateCheckoutResult> CreateSubscriptionCheckoutAsync(
        Guid userId,
        string planCode,
        string customerName,
        string customerEmail,
        CancellationToken cancellationToken = default);

    Task<StripeCheckoutConfirmationResult> ConfirmCheckoutAsync(
        string sessionId,
        CancellationToken cancellationToken = default);
}
