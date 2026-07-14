using FPTUniRAG.BusinessLayer.Payments.Momo;

namespace FPTUniRAG.Endpoints;

public static class MomoPaymentEndpoints
{
    public static IEndpointRouteBuilder MapMomoPaymentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/payments/momo/ipn", async (
            HttpRequest request,
            IMomoPaymentService momoPaymentService,
            CancellationToken cancellationToken) =>
        {
            var form = await request.ReadFromJsonAsync<Dictionary<string, string?>>(cancellationToken: cancellationToken)
                ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            var result = await momoPaymentService.ProcessIpnAsync(form, cancellationToken);
            return Results.Ok(new
            {
                resultCode = result.Succeeded ? 0 : 1,
                message = result.Message
            });
        }).AllowAnonymous();

        return endpoints;
    }
}
