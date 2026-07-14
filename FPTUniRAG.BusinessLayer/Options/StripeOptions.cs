namespace FPTUniRAG.BusinessLayer.Options;

public sealed class StripeOptions
{
    public string BaseUrl { get; set; } = "https://api.stripe.com/v1";

    public string SecretKey { get; set; } = string.Empty;

    public string Currency { get; set; } = "vnd";

    public string PublicBaseUrl { get; set; } = "https://localhost:7268";

    public string SuccessPath { get; set; } = "/payments/stripe/return";

    public string CancelPath { get; set; } = "/StudentPlans";
}
