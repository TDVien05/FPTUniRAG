using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;

namespace FPTUniRAG.DataAccessLayer.Repositories.Payments;

public sealed class PaymentRepository(AppDbContext context) : IPaymentRepository
{
    public Task<SubscriptionPlan?> GetActivePlanAsync(string planCode, CancellationToken cancellationToken = default) =>
        context.SubscriptionPlans.FirstOrDefaultAsync(p => p.IsActive && p.PlanCode == planCode, cancellationToken);
    public Task SavePlanAsync(SubscriptionPlan plan, CancellationToken cancellationToken = default) => SaveAsync(cancellationToken);
    public async Task AddMomoTransactionAsync(MomoPaymentTransaction transaction, CancellationToken cancellationToken = default) { context.MomoPaymentTransactions.Add(transaction); await SaveAsync(cancellationToken); }
    public Task SaveMomoTransactionAsync(MomoPaymentTransaction transaction, CancellationToken cancellationToken = default) => SaveAsync(cancellationToken);
    public Task<MomoPaymentTransaction?> FindMomoTransactionAsync(string orderId, string requestId, CancellationToken cancellationToken = default) =>
        context.MomoPaymentTransactions.Include(t => t.Plan).SingleOrDefaultAsync(t => t.OrderId == orderId && t.RequestId == requestId, cancellationToken);

    public async Task ActivateMomoSubscriptionAsync(MomoPaymentTransaction transaction, DateTime now, CancellationToken cancellationToken = default)
    {
        await using var dbTransaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var active = await context.StudentSubscriptions.Where(s => s.UserId == transaction.UserId && s.SubscriptionStatus == "active").ToListAsync(cancellationToken);
        foreach (var item in active) { item.SubscriptionStatus = "replaced"; item.CanceledAt = now; item.ExpiresAt ??= now; item.Notes = $"Replaced by MoMo payment order {transaction.OrderId}."; }
        context.StudentSubscriptions.Add(new StudentSubscription { UserId = transaction.UserId, PlanId = transaction.PlanId, SubscriptionStatus = "active", StartedAt = now, PurchasedAt = now, ExpiresAt = now.AddMonths(1), AutoRenew = false, Notes = $"Activated from MoMo sandbox payment order {transaction.OrderId}." });
        transaction.PaymentStatus = "paid"; transaction.ConfirmedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        await SaveAsync(cancellationToken); await dbTransaction.CommitAsync(cancellationToken);
    }

    public async Task AddStripeTransactionAsync(StripeCheckoutTransaction transaction, CancellationToken cancellationToken = default) { context.StripeCheckoutTransactions.Add(transaction); await SaveAsync(cancellationToken); }
    public Task SaveStripeTransactionAsync(StripeCheckoutTransaction transaction, CancellationToken cancellationToken = default) => SaveAsync(cancellationToken);
    public Task<StripeCheckoutTransaction?> FindStripeTransactionAsync(string checkoutId, CancellationToken cancellationToken = default) =>
        context.StripeCheckoutTransactions.Include(t => t.Plan).SingleOrDefaultAsync(t => t.CheckoutId == checkoutId, cancellationToken);

    public async Task<StripeActivationRecord> GetStripeActivationAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var current = await context.StudentSubscriptions.Where(s => s.UserId == userId && s.SubscriptionStatus == "active").OrderByDescending(s => s.PurchasedAt).FirstOrDefaultAsync(cancellationToken);
        var previousRaw = current is not null && string.IsNullOrWhiteSpace(current.StripeSubscriptionId)
            ? await context.StripeCheckoutTransactions.AsNoTracking().Where(t => t.UserId == userId && t.PaymentStatus == "paid").OrderByDescending(t => t.ConfirmedAt ?? t.CreatedAt).Select(t => t.RawResponseJson).FirstOrDefaultAsync(cancellationToken)
            : null;
        return new StripeActivationRecord(current, previousRaw);
    }

    public async Task ActivateStripeSubscriptionAsync(StripeCheckoutTransaction transaction, StudentSubscription? current, string checkoutId, string? stripeSubscriptionId, DateTime now, CancellationToken cancellationToken = default)
    {
        await using var dbTransaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (current is null) context.StudentSubscriptions.Add(new StudentSubscription { UserId = transaction.UserId, PlanId = transaction.PlanId, SubscriptionStatus = "active", StartedAt = now, PurchasedAt = now, ExpiresAt = now.AddMonths(1), StripeSubscriptionId = stripeSubscriptionId, AutoRenew = false, Notes = $"Activated from Stripe test checkout {checkoutId}." });
        else { current.PlanId = transaction.PlanId; current.SubscriptionStatus = "active"; current.StartedAt = now; current.PurchasedAt = now; current.ExpiresAt = now.AddMonths(1); current.CanceledAt = null; current.StripeSubscriptionId = stripeSubscriptionId; current.AutoRenew = false; current.GrantedBy = null; current.Notes = $"Replaced the previous exhausted subscription from Stripe test checkout {checkoutId}."; }
        transaction.PaymentStatus = "paid"; transaction.ConfirmedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        await SaveAsync(cancellationToken); await dbTransaction.CommitAsync(cancellationToken);
    }

    private async Task SaveAsync(CancellationToken cancellationToken) => await context.SaveChangesAsync(cancellationToken);
}
