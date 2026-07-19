using FPTUniRAG.DataAccessLayer.Entities;

namespace FPTUniRAG.DataAccessLayer.Repositories.Payments;

public interface IPaymentRepository
{
    Task<SubscriptionPlan?> GetActivePlanAsync(string planCode, CancellationToken cancellationToken = default);
    Task SavePlanAsync(SubscriptionPlan plan, CancellationToken cancellationToken = default);
    Task AddStripeTransactionAsync(StripeCheckoutTransaction transaction, CancellationToken cancellationToken = default);
    Task SaveStripeTransactionAsync(StripeCheckoutTransaction transaction, CancellationToken cancellationToken = default);
    Task<StripeCheckoutTransaction?> FindStripeTransactionAsync(string checkoutId, CancellationToken cancellationToken = default);
    Task<StripeActivationRecord> GetStripeActivationAsync(Guid userId, CancellationToken cancellationToken = default);
    Task ActivateStripeSubscriptionAsync(StripeCheckoutTransaction transaction, StudentSubscription? currentSubscription,
        string checkoutId, string? stripeSubscriptionId, DateTime now, CancellationToken cancellationToken = default);
}

public sealed record StripeActivationRecord(StudentSubscription? CurrentSubscription, string? PreviousRawResponseJson);
