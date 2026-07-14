namespace FPTUniRAG.BusinessLayer.Services;

public sealed record StripeCreateCheckoutResult(
    bool Succeeded,
    string? SessionId,
    string? CheckoutUrl,
    string Message)
{
    public static StripeCreateCheckoutResult Success(string sessionId, string checkoutUrl) =>
        new(true, sessionId, checkoutUrl, "Checkout session created.");

    public static StripeCreateCheckoutResult Failure(string message) =>
        new(false, null, null, message);
}

public sealed record StripeCheckoutConfirmationResult(
    bool Succeeded,
    bool IsPaid,
    string Title,
    string Message,
    string? SessionId)
{
    public static StripeCheckoutConfirmationResult Failure(string title, string message, string? sessionId = null) =>
        new(false, false, title, message, sessionId);

    public static StripeCheckoutConfirmationResult Pending(string title, string message, string? sessionId = null) =>
        new(true, false, title, message, sessionId);

    public static StripeCheckoutConfirmationResult Paid(string title, string message, string? sessionId = null) =>
        new(true, true, title, message, sessionId);
}

public sealed record StripePlanPriceProvisionResult(
    bool Succeeded,
    string? StripePriceId,
    string Message)
{
    public static StripePlanPriceProvisionResult Success(string stripePriceId) =>
        new(true, stripePriceId, "Stripe price generated.");

    public static StripePlanPriceProvisionResult Failure(string message) =>
        new(false, null, message);
}
