using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;

namespace FPTUniRAG.DataAccessLayer.Repositories.Payments;

public sealed class PaymentRepository(AppDbContext context) : IPaymentRepository
{
    public Task<SubscriptionPlan?> GetActivePlanAsync(string planCode, CancellationToken cancellationToken = default) =>
        context.SubscriptionPlans.FirstOrDefaultAsync(p => p.IsActive && p.PlanCode == planCode, cancellationToken);
    public Task SavePlanAsync(SubscriptionPlan plan, CancellationToken cancellationToken = default) => SaveAsync(cancellationToken);
    public async Task AddStripeTransactionAsync(StripeCheckoutTransaction transaction, CancellationToken cancellationToken = default) { context.StripeCheckoutTransactions.Add(transaction); await SaveAsync(cancellationToken); }
    public Task SaveStripeTransactionAsync(StripeCheckoutTransaction transaction, CancellationToken cancellationToken = default) => SaveAsync(cancellationToken);
    public Task<StripeCheckoutTransaction?> FindStripeTransactionAsync(string checkoutId, CancellationToken cancellationToken = default) =>
        context.StripeCheckoutTransactions.Include(t => t.Plan).SingleOrDefaultAsync(t => t.CheckoutId == checkoutId, cancellationToken);

    public async Task<StripeActivationRecord> GetStripeActivationAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var current = await context.StudentSubscriptions.Include(s => s.Plan).Where(s => s.UserId == userId && s.SubscriptionStatus == "active").OrderByDescending(s => s.PurchasedAt).FirstOrDefaultAsync(cancellationToken);
        var previousRaw = current is not null && string.IsNullOrWhiteSpace(current.StripeSubscriptionId)
            ? await context.StripeCheckoutTransactions.AsNoTracking().Where(t => t.UserId == userId && t.PaymentStatus == "paid").OrderByDescending(t => t.ConfirmedAt ?? t.CreatedAt).Select(t => t.RawResponseJson).FirstOrDefaultAsync(cancellationToken)
            : null;
        return new StripeActivationRecord(current, previousRaw);
    }

    public async Task ActivateStripeSubscriptionAsync(StripeCheckoutTransaction transaction, StudentSubscription? current, string checkoutId, string? stripeSubscriptionId, DateTime now, CancellationToken cancellationToken = default)
    {
        await using var dbTransaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // An upgrade to a strictly higher-priced plan takes effect immediately and carries over any
        // unused tokens from the old plan's current billing cycle; the old cycle's expiry is kept so
        // the carryover cannot roll into a later cycle. Any other purchase (renewal, downgrade, or
        // replacing an exhausted plan) starts a fresh cycle with no carryover.
        var isUpgrade = current is not null
            && current.PlanId != transaction.PlanId
            && (current.ExpiresAt is null || current.ExpiresAt > now)
            && current.Plan.MonthlyPrice < transaction.Plan.MonthlyPrice;

        var carryoverTokens = 0L;
        if (isUpgrade && current!.Plan.MonthlyTokenLimit is > 0)
        {
            var usedThisMonth = (await context.StudentTokenUsageCurrentMonths.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == transaction.UserId, cancellationToken))?.TotalTokensUsedThisMonth ?? 0m;
            carryoverTokens = Math.Max(0L, current.Plan.MonthlyTokenLimit.Value - (long)usedThisMonth);
        }

        if (current is null)
        {
            context.StudentSubscriptions.Add(new StudentSubscription { UserId = transaction.UserId, PlanId = transaction.PlanId, SubscriptionStatus = "active", StartedAt = now, PurchasedAt = now, ExpiresAt = now.AddMonths(1), CarryoverTokens = 0, StripeSubscriptionId = stripeSubscriptionId, AutoRenew = false, Notes = $"Activated from Stripe test checkout {checkoutId}." });
        }
        else
        {
            current.PlanId = transaction.PlanId;
            current.SubscriptionStatus = "active";
            current.StartedAt = now;
            current.PurchasedAt = now;
            current.ExpiresAt = carryoverTokens > 0 ? current.ExpiresAt : now.AddMonths(1);
            current.CarryoverTokens = carryoverTokens;
            current.CanceledAt = null;
            current.StripeSubscriptionId = stripeSubscriptionId;
            current.AutoRenew = false;
            current.GrantedBy = null;
            current.Notes = carryoverTokens > 0
                ? $"Upgraded from a previous plan via Stripe test checkout {checkoutId}; carried over {carryoverTokens:N0} unused tokens until this billing cycle ends."
                : $"Replaced the previous exhausted subscription from Stripe test checkout {checkoutId}.";
        }
        transaction.PaymentStatus = "paid"; transaction.ConfirmedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        await SaveAsync(cancellationToken); await dbTransaction.CommitAsync(cancellationToken);
    }

    private async Task SaveAsync(CancellationToken cancellationToken) => await context.SaveChangesAsync(cancellationToken);
}
