namespace FPTUniRAG.BusinessLayer.Payments.Momo;

public sealed class MomoOptions
{
    public string BaseUrl { get; set; } = "https://test-payment.momo.vn";

    public string CreatePaymentPath { get; set; } = "/v2/gateway/api/create";

    public string PartnerCode { get; set; } = string.Empty;

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string PartnerName { get; set; } = "FPT UniRAG Sandbox";

    public string StoreId { get; set; } = "FPTUniRAG";

    public string RequestType { get; set; } = "captureWallet";

    public string Lang { get; set; } = "vi";

    public string PublicBaseUrl { get; set; } = "https://localhost:5001";

    public string ReturnPath { get; set; } = "/payments/momo/return";

    public string IpnPath { get; set; } = "/api/payments/momo/ipn";
}
