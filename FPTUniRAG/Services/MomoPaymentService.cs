using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.DataAccessLayer.Entities;
using FPTUniRAG.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FPTUniRAG.Services;

public sealed class MomoPaymentService : IMomoPaymentService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _dbContext;
    private readonly HttpClient _httpClient;
    private readonly MomoOptions _options;
    private readonly ILogger<MomoPaymentService> _logger;

    public MomoPaymentService(
        AppDbContext dbContext,
        HttpClient httpClient,
        IOptions<MomoOptions> options,
        ILogger<MomoPaymentService> logger)
    {
        _dbContext = dbContext;
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MomoCreatePaymentResult> CreateSubscriptionPaymentAsync(
        Guid userId,
        string planCode,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions();

        var normalizedPlanCode = planCode.Trim().ToLowerInvariant();
        var plan = await _dbContext.SubscriptionPlans
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.IsActive && candidate.PlanCode == normalizedPlanCode, cancellationToken);

        if (plan is null)
        {
            return MomoCreatePaymentResult.Failure("The selected plan is not available right now.");
        }

        var now = DateTime.UtcNow;
        var orderId = $"MOMO-{userId:N}-{now:yyyyMMddHHmmssfff}";
        var requestId = Guid.NewGuid().ToString("N");
        var orderInfo = $"FPT UniRAG subscription purchase for plan {plan.PlanName}";
        var redirectUrl = BuildAbsoluteUrl(_options.ReturnPath);
        var ipnUrl = BuildAbsoluteUrl(_options.IpnPath);
        var extraData = Convert.ToBase64String(Encoding.UTF8.GetBytes($"userId={userId:N}&planCode={plan.PlanCode}"));
        var amount = Decimal.ToInt64(decimal.Round(plan.MonthlyPrice, 0, MidpointRounding.AwayFromZero));

        var requestPayload = new Dictionary<string, object?>
        {
            ["partnerCode"] = _options.PartnerCode,
            ["partnerName"] = _options.PartnerName,
            ["storeId"] = _options.StoreId,
            ["requestId"] = requestId,
            ["amount"] = amount.ToString(),
            ["orderId"] = orderId,
            ["orderInfo"] = orderInfo,
            ["redirectUrl"] = redirectUrl,
            ["ipnUrl"] = ipnUrl,
            ["lang"] = _options.Lang,
            ["requestType"] = _options.RequestType,
            ["autoCapture"] = true,
            ["extraData"] = extraData
        };

        requestPayload["signature"] = ComputeSignature(BuildCreateSignatureSource(
            amount.ToString(),
            extraData,
            ipnUrl,
            orderId,
            orderInfo,
            redirectUrl,
            requestId));

        var transaction = new MomoPaymentTransaction
        {
            UserId = userId,
            PlanId = plan.PlanId,
            OrderId = orderId,
            RequestId = requestId,
            Amount = plan.MonthlyPrice,
            PaymentStatus = "pending",
            RawRequestJson = JsonSerializer.Serialize(requestPayload, SerializerOptions),
            CreatedAt = CreateDatabaseTimestamp()
        };

        _dbContext.MomoPaymentTransactions.Add(transaction);
        await _dbContext.SaveChangesAsync(cancellationToken);

        using var response = await _httpClient.PostAsJsonAsync(_options.CreatePaymentPath, requestPayload, SerializerOptions, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        transaction.RawResponseJson = responseText;

        if (!response.IsSuccessStatusCode)
        {
            transaction.PaymentStatus = "failed";
            transaction.ProviderMessage = "MoMo create payment request failed.";
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("MoMo create payment returned non-success status {StatusCode}: {Payload}", response.StatusCode, responseText);
            return MomoCreatePaymentResult.Failure("Unable to initialize MoMo payment right now.");
        }

        var createResponse = JsonSerializer.Deserialize<MomoCreateResponse>(responseText, SerializerOptions);
        if (createResponse is null || string.IsNullOrWhiteSpace(createResponse.PayUrl))
        {
            transaction.PaymentStatus = "failed";
            transaction.ResultCode = createResponse?.ResultCode;
            transaction.ProviderMessage = createResponse?.Message ?? "MoMo returned an empty payment URL.";
            await _dbContext.SaveChangesAsync(cancellationToken);
            return MomoCreatePaymentResult.Failure("MoMo did not return a valid payment URL.");
        }

        transaction.PayUrl = createResponse.PayUrl;
        transaction.ResultCode = createResponse.ResultCode;
        transaction.ProviderMessage = createResponse.Message;

        if (createResponse.ResultCode != 0)
        {
            transaction.PaymentStatus = "failed";
            await _dbContext.SaveChangesAsync(cancellationToken);
            return MomoCreatePaymentResult.Failure(createResponse.Message ?? "Unable to initialize MoMo payment right now.");
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return MomoCreatePaymentResult.Success(createResponse.PayUrl);
    }

    public Task<MomoPaymentCallbackResult> ProcessReturnAsync(
        IReadOnlyDictionary<string, string?> parameters,
        CancellationToken cancellationToken = default)
    {
        return ProcessCallbackAsync(parameters, "browser-return", cancellationToken);
    }

    public Task<MomoPaymentCallbackResult> ProcessIpnAsync(
        IReadOnlyDictionary<string, string?> parameters,
        CancellationToken cancellationToken = default)
    {
        return ProcessCallbackAsync(parameters, "ipn", cancellationToken);
    }

    private async Task<MomoPaymentCallbackResult> ProcessCallbackAsync(
        IReadOnlyDictionary<string, string?> parameters,
        string source,
        CancellationToken cancellationToken)
    {
        ValidateOptions();

        var signature = GetValue(parameters, "signature");
        var orderId = GetValue(parameters, "orderId");
        var requestId = GetValue(parameters, "requestId");
        var resultCodeText = GetValue(parameters, "resultCode");
        var amount = GetValue(parameters, "amount");
        var orderInfo = GetValue(parameters, "orderInfo");
        var orderType = GetValue(parameters, "orderType");
        var transIdText = GetValue(parameters, "transId");
        var message = GetValue(parameters, "message");
        var payType = GetValue(parameters, "payType");
        var extraData = GetValue(parameters, "extraData");
        var responseTime = GetValue(parameters, "responseTime");

        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(requestId))
        {
            return MomoPaymentCallbackResult.Failure("Payment verification failed", "MoMo callback is missing required fields.", orderId);
        }

        var rawSignatureSource = BuildCallbackSignatureSource(
            amount,
            extraData,
            message,
            orderId,
            orderInfo,
            orderType,
            payType,
            requestId,
            responseTime,
            resultCodeText,
            transIdText);

        var expectedSignature = ComputeSignature(rawSignatureSource);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(signature),
                Encoding.UTF8.GetBytes(expectedSignature)))
        {
            _logger.LogWarning("Rejected MoMo callback from {Source} due to invalid signature. OrderId={OrderId}", source, orderId);
            return MomoPaymentCallbackResult.Failure("Payment verification failed", "The MoMo signature is invalid.", orderId);
        }

        var transaction = await _dbContext.MomoPaymentTransactions
            .Include(item => item.Plan)
            .SingleOrDefaultAsync(item => item.OrderId == orderId && item.RequestId == requestId, cancellationToken);

        if (transaction is null)
        {
            return MomoPaymentCallbackResult.Failure("Payment verification failed", "The payment transaction no longer exists.", orderId);
        }

        var resultCode = long.TryParse(resultCodeText, out var parsedResultCode)
            ? parsedResultCode
            : -1;
        var providerTransactionId = long.TryParse(transIdText, out var parsedTransId)
            ? parsedTransId
            : (long?)null;

        transaction.ResultCode = resultCode;
        transaction.ProviderMessage = message;
        transaction.ProviderTransactionId = providerTransactionId;
        transaction.RawResponseJson = JsonSerializer.Serialize(parameters, SerializerOptions);

        if (transaction.PaymentStatus == "paid")
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return MomoPaymentCallbackResult.Paid("Payment already confirmed", $"The purchase for {transaction.Plan.PlanName} was already activated.", orderId);
        }

        if (resultCode != 0)
        {
            transaction.PaymentStatus = "failed";
            transaction.ConfirmedAt = CreateDatabaseTimestamp();
            await _dbContext.SaveChangesAsync(cancellationToken);
            return MomoPaymentCallbackResult.Failure("Payment not completed", message ?? "MoMo reported that the payment was not successful.", orderId);
        }

        await using var dbTransaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var activeSubscriptions = await _dbContext.StudentSubscriptions
            .Where(subscription => subscription.UserId == transaction.UserId && subscription.SubscriptionStatus == "active")
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var activeSubscription in activeSubscriptions)
        {
            activeSubscription.SubscriptionStatus = "replaced";
            activeSubscription.CanceledAt = now;
            activeSubscription.ExpiresAt ??= now;
            activeSubscription.Notes = $"Replaced by MoMo payment order {transaction.OrderId}.";
        }

        _dbContext.StudentSubscriptions.Add(new StudentSubscription
        {
            UserId = transaction.UserId,
            PlanId = transaction.PlanId,
            SubscriptionStatus = "active",
            StartedAt = now,
            PurchasedAt = now,
            ExpiresAt = now.AddMonths(1),
            AutoRenew = false,
            Notes = $"Activated from MoMo sandbox payment order {transaction.OrderId}."
        });

        transaction.PaymentStatus = "paid";
        transaction.ConfirmedAt = CreateDatabaseTimestamp();

        await _dbContext.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);

        return MomoPaymentCallbackResult.Paid(
            "Payment successful",
            $"Your {transaction.Plan.PlanName} plan is now active. You can continue chatting immediately.",
            orderId);
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.PartnerCode)
            || string.IsNullOrWhiteSpace(_options.AccessKey)
            || string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            throw new InvalidOperationException("MoMo sandbox settings are incomplete. Configure the Momo section in appsettings.json.");
        }
    }

    private string ComputeSignature(string rawSignature)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.SecretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawSignature));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string BuildCreateSignatureSource(
        string amount,
        string extraData,
        string ipnUrl,
        string orderId,
        string orderInfo,
        string redirectUrl,
        string requestId)
    {
        return string.Join("&", [
            $"accessKey={_options.AccessKey}",
            $"amount={amount}",
            $"extraData={extraData}",
            $"ipnUrl={ipnUrl}",
            $"orderId={orderId}",
            $"orderInfo={orderInfo}",
            $"partnerCode={_options.PartnerCode}",
            $"redirectUrl={redirectUrl}",
            $"requestId={requestId}",
            $"requestType={_options.RequestType}"
        ]);
    }

    private string BuildCallbackSignatureSource(
        string amount,
        string extraData,
        string message,
        string orderId,
        string orderInfo,
        string orderType,
        string payType,
        string requestId,
        string responseTime,
        string resultCode,
        string transId)
    {
        return string.Join("&", [
            $"accessKey={_options.AccessKey}",
            $"amount={amount}",
            $"extraData={extraData}",
            $"message={message}",
            $"orderId={orderId}",
            $"orderInfo={orderInfo}",
            $"orderType={orderType}",
            $"partnerCode={_options.PartnerCode}",
            $"payType={payType}",
            $"requestId={requestId}",
            $"responseTime={responseTime}",
            $"resultCode={resultCode}",
            $"transId={transId}"
        ]);
    }

    private string BuildAbsoluteUrl(string path)
    {
        return $"{_options.PublicBaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
    }

    private static string GetValue(IReadOnlyDictionary<string, string?> parameters, string key)
    {
        return parameters.TryGetValue(key, out var value)
            ? value?.Trim() ?? string.Empty
            : string.Empty;
    }

    private static DateTime CreateDatabaseTimestamp()
    {
        return DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
    }

    private sealed record MomoCreateResponse(
        long ResultCode,
        string? Message,
        string? PayUrl);
}
