namespace FPTUniRAG.BusinessLayer.Services;

public interface IMomoPaymentService
{
    Task<MomoCreatePaymentResult> CreateSubscriptionPaymentAsync(
        Guid userId,
        string planCode,
        CancellationToken cancellationToken = default);

    Task<MomoPaymentCallbackResult> ProcessReturnAsync(
        IReadOnlyDictionary<string, string?> parameters,
        CancellationToken cancellationToken = default);

    Task<MomoPaymentCallbackResult> ProcessIpnAsync(
        IReadOnlyDictionary<string, string?> parameters,
        CancellationToken cancellationToken = default);
}
