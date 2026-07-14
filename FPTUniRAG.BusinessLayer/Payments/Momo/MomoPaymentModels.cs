namespace FPTUniRAG.BusinessLayer.Payments.Momo;

public sealed record MomoCreatePaymentResult(
    bool Succeeded,
    string? RedirectUrl,
    string Message)
{
    public static MomoCreatePaymentResult Success(string redirectUrl) =>
        new(true, redirectUrl, "Payment session created.");

    public static MomoCreatePaymentResult Failure(string message) =>
        new(false, null, message);
}

public sealed record MomoPaymentCallbackResult(
    bool Succeeded,
    bool IsPaid,
    string Title,
    string Message,
    string? OrderId)
{
    public static MomoPaymentCallbackResult Failure(string title, string message, string? orderId = null) =>
        new(false, false, title, message, orderId);

    public static MomoPaymentCallbackResult Paid(string title, string message, string? orderId = null) =>
        new(true, true, title, message, orderId);

    public static MomoPaymentCallbackResult Pending(string title, string message, string? orderId = null) =>
        new(true, false, title, message, orderId);
}
