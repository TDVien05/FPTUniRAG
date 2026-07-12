using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.DataAccessLayer.Entities;
using FPTUniRAG.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FPTUniRAG.Services;

public sealed class StripePaymentService : IStripePaymentService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _dbContext;
    private readonly HttpClient _httpClient;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly StripeOptions _options;
    private readonly ILogger<StripePaymentService> _logger;

    public StripePaymentService(
        AppDbContext dbContext,
        HttpClient httpClient,
        IHttpContextAccessor httpContextAccessor,
        IOptions<StripeOptions> options,
        ILogger<StripePaymentService> logger)
    {
        _dbContext = dbContext;
        _httpClient = httpClient;
        _httpContextAccessor = httpContextAccessor;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<StripePlanPriceProvisionResult> EnsurePlanPriceAsync(
        Guid planId,
        string planCode,
        string planName,
        string? description,
        decimal monthlyPrice,
        string? existingStripePriceId,
        CancellationToken cancellationToken = default)
    {
        if (!HasConfiguredSecretKey())
        {
            return StripePlanPriceProvisionResult.Failure(BuildMissingSecretKeyMessage());
        }

        if (monthlyPrice < 0)
        {
            return StripePlanPriceProvisionResult.Failure("Monthly price cannot be negative.");
        }

        if (!TryConvertAmountToStripeUnits(monthlyPrice, out var unitAmount))
        {
            return StripePlanPriceProvisionResult.Failure("Monthly price is invalid for Stripe. Use a non-negative whole-number VND amount.");
        }

        var productId = await ResolveOrCreateProductAsync(
            planId,
            planCode,
            planName,
            description,
            existingStripePriceId,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(productId))
        {
            return StripePlanPriceProvisionResult.Failure("Unable to create a Stripe product for this plan.");
        }

        var existingPrice = await TryGetPriceAsync(existingStripePriceId, cancellationToken);
        if (existingPrice is not null &&
            IsValidStripePriceId(existingPrice.Id) &&
            string.Equals(existingPrice.ProductId, productId, StringComparison.OrdinalIgnoreCase) &&
            existingPrice.UnitAmount == unitAmount &&
            string.Equals(existingPrice.Currency, _options.Currency.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existingPrice.RecurringInterval, "month", StringComparison.OrdinalIgnoreCase))
        {
            return StripePlanPriceProvisionResult.Success(existingPrice.Id!);
        }

        var priceParameters = new List<KeyValuePair<string, string>>
        {
            new("currency", _options.Currency.Trim().ToLowerInvariant()),
            new("unit_amount", unitAmount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("product", productId),
            new("recurring[interval]", "month"),
            new("metadata[localPlanId]", planId.ToString("N")),
            new("metadata[localPlanCode]", planCode),
            new("metadata[localPlanName]", planName)
        };

        using var response = await SendStripeFormAsync(HttpMethod.Post, "prices", priceParameters, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Stripe create price returned non-success status {StatusCode}: {Payload}", response.StatusCode, responseText);
            return StripePlanPriceProvisionResult.Failure(BuildCreateCheckoutFailureMessage(response.StatusCode, responseText));
        }

        var price = JsonSerializer.Deserialize<StripePriceResponse>(responseText, SerializerOptions);
        if (price is null || !IsValidStripePriceId(price.Id))
        {
            return StripePlanPriceProvisionResult.Failure("Stripe did not return a valid recurring price ID for this plan.");
        }

        return StripePlanPriceProvisionResult.Success(price.Id!);
    }

    public async Task<StripeCreateCheckoutResult> CreateSubscriptionCheckoutAsync(
        Guid userId,
        string planCode,
        string customerName,
        string customerEmail,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions();

        var normalizedPlanCode = planCode.Trim().ToLowerInvariant();
        var plan = await _dbContext.SubscriptionPlans
            .SingleOrDefaultAsync(
                candidate => candidate.IsActive && candidate.PlanCode == normalizedPlanCode,
                cancellationToken);

        if (plan is null)
        {
            return StripeCreateCheckoutResult.Failure("The selected plan is not available right now.");
        }

        if (!IsValidStripePriceId(plan.StripePriceId))
        {
            var priceResult = await EnsurePlanPriceAsync(
                plan.PlanId,
                plan.PlanCode,
                plan.PlanName,
                plan.Description,
                plan.MonthlyPrice,
                plan.StripePriceId,
                cancellationToken);
            if (!priceResult.Succeeded || !IsValidStripePriceId(priceResult.StripePriceId))
            {
                return StripeCreateCheckoutResult.Failure(priceResult.Message);
            }

            plan.StripePriceId = priceResult.StripePriceId;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var requestParameters = new List<KeyValuePair<string, string>>
        {
            new("mode", "subscription"),
            new("success_url", BuildAbsoluteUrl(_options.SuccessPath, "session_id={CHECKOUT_SESSION_ID}")),
            new("cancel_url", BuildAbsoluteUrl(_options.CancelPath)),
            new("client_reference_id", userId.ToString("N")),
            new("line_items[0][price]", plan.StripePriceId!.Trim()),
            new("line_items[0][quantity]", "1"),
            new("metadata[localUserId]", userId.ToString("N")),
            new("metadata[localPlanId]", plan.PlanId.ToString("N")),
            new("metadata[localPlanCode]", plan.PlanCode)
        };

        if (!string.IsNullOrWhiteSpace(customerEmail))
        {
            requestParameters.Add(new("customer_email", customerEmail.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(customerName))
        {
            requestParameters.Add(new("metadata[customerName]", customerName.Trim()));
        }

        var requestPayload = SerializeFormPayload(requestParameters);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "checkout/sessions")
        {
            Content = new FormUrlEncodedContent(requestParameters)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.SecretKey);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Stripe create checkout returned non-success status {StatusCode}: {Payload}", response.StatusCode, responseText);
            return StripeCreateCheckoutResult.Failure(BuildCreateCheckoutFailureMessage(response.StatusCode, responseText));
        }

        var session = JsonSerializer.Deserialize<StripeCheckoutSessionResponse>(responseText, SerializerOptions);
        if (session is null || string.IsNullOrWhiteSpace(session.Id) || string.IsNullOrWhiteSpace(session.Url))
        {
            return StripeCreateCheckoutResult.Failure("Stripe did not return a valid checkout session.");
        }

        var transaction = new StripeCheckoutTransaction
        {
            UserId = userId,
            PlanId = plan.PlanId,
            CheckoutId = session.Id,
            CheckoutUrl = session.Url,
            PaymentStatus = session.Status ?? session.PaymentStatus ?? "open",
            Amount = plan.MonthlyPrice,
            StripePriceId = plan.StripePriceId,
            RawRequestJson = requestPayload,
            RawResponseJson = responseText,
            CreatedAt = CreateDatabaseTimestamp()
        };

        _dbContext.StripeCheckoutTransactions.Add(transaction);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return StripeCreateCheckoutResult.Success(session.Id, session.Url);
    }

    public async Task<StripeCheckoutConfirmationResult> ConfirmCheckoutAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions();

        var normalizedSessionId = sessionId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedSessionId))
        {
            return StripeCheckoutConfirmationResult.Failure("Session missing", "Stripe did not return a checkout session identifier.");
        }

        var transaction = await _dbContext.StripeCheckoutTransactions
            .Include(item => item.Plan)
            .SingleOrDefaultAsync(item => item.CheckoutId == normalizedSessionId, cancellationToken);

        if (transaction is null)
        {
            return StripeCheckoutConfirmationResult.Failure("Session not found", "The Stripe checkout session no longer exists.", normalizedSessionId);
        }

        var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"checkout/sessions/{normalizedSessionId}");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.SecretKey);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        var wasAlreadyPaid = string.Equals(transaction.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase);
        transaction.RawResponseJson = responseText;

        if (!response.IsSuccessStatusCode)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("Stripe get checkout session returned non-success status {StatusCode}: {Payload}", response.StatusCode, responseText);
            return StripeCheckoutConfirmationResult.Failure("Payment verification failed", "Unable to verify the Stripe checkout status right now.", normalizedSessionId);
        }

        var session = JsonSerializer.Deserialize<StripeCheckoutSessionResponse>(responseText, SerializerOptions);
        if (session is null)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return StripeCheckoutConfirmationResult.Failure("Payment verification failed", "Stripe returned an unreadable checkout response.", normalizedSessionId);
        }

        var status = session.Status?.Trim().ToLowerInvariant() ?? "unknown";
        var paymentStatus = session.PaymentStatus?.Trim().ToLowerInvariant();
        transaction.PaymentStatus = status == "complete" && paymentStatus == "paid" ? "paid" : status;

        if (wasAlreadyPaid)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return StripeCheckoutConfirmationResult.Paid("Payment already confirmed", $"The purchase for {transaction.Plan.PlanName} was already activated.", normalizedSessionId);
        }

        if (status == "complete")
        {
            var subscriptionStatus = await GetSubscriptionStatusAsync(session.SubscriptionId, cancellationToken);
            if (paymentStatus == "paid" || paymentStatus == "no_payment_required" || subscriptionStatus is "active" or "trialing")
            {
                await ActivateSubscriptionAsync(transaction, normalizedSessionId, session.SubscriptionId, cancellationToken);
                return StripeCheckoutConfirmationResult.Paid(
                    "Payment successful",
                    $"Your {transaction.Plan.PlanName} plan is now active. You can continue chatting immediately.",
                    normalizedSessionId);
            }
        }

        transaction.ConfirmedAt = status == "expired"
            ? CreateDatabaseTimestamp()
            : transaction.ConfirmedAt;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return status switch
        {
            "open" => StripeCheckoutConfirmationResult.Pending(
                "Payment still processing",
                "Stripe has not finished confirming this payment yet. Please refresh again in a moment.",
                normalizedSessionId),
            "expired" => StripeCheckoutConfirmationResult.Failure(
                "Checkout expired",
                "This Stripe checkout session has expired. Start a new purchase from the plans page.",
                normalizedSessionId),
            "complete" => StripeCheckoutConfirmationResult.Pending(
                "Subscription still processing",
                "Stripe completed checkout, but the subscription is not active yet. Please refresh again in a moment.",
                normalizedSessionId),
            _ => StripeCheckoutConfirmationResult.Pending(
                "Payment update",
                $"Stripe checkout status is currently '{status}'.",
                normalizedSessionId)
        };
    }

    private async Task ActivateSubscriptionAsync(
        StripeCheckoutTransaction transaction,
        string checkoutId,
        string? stripeSubscriptionId,
        CancellationToken cancellationToken)
    {
        if (transaction.PaymentStatus == "paid" && transaction.ConfirmedAt is not null)
        {
            return;
        }

        await using var dbTransaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var currentSubscription = await _dbContext.StudentSubscriptions
            .Where(subscription => subscription.UserId == transaction.UserId && subscription.SubscriptionStatus == "active")
            .OrderByDescending(subscription => subscription.PurchasedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (currentSubscription is null)
        {
            _dbContext.StudentSubscriptions.Add(new StudentSubscription
            {
                UserId = transaction.UserId,
                PlanId = transaction.PlanId,
                SubscriptionStatus = "active",
                StartedAt = now,
                PurchasedAt = now,
                ExpiresAt = now.AddMonths(1),
                StripeSubscriptionId = stripeSubscriptionId,
                AutoRenew = false,
                Notes = $"Activated from Stripe test checkout {checkoutId}."
            });
        }
        else
        {
            var previousStripeSubscriptionId = currentSubscription.StripeSubscriptionId;
            if (string.IsNullOrWhiteSpace(previousStripeSubscriptionId))
            {
                var previousTransaction = await _dbContext.StripeCheckoutTransactions
                    .AsNoTracking()
                    .Where(item => item.UserId == transaction.UserId && item.PaymentStatus == "paid")
                    .OrderByDescending(item => item.ConfirmedAt ?? item.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);
                previousStripeSubscriptionId = ExtractStripeSubscriptionId(previousTransaction?.RawResponseJson);
            }

            if (!await CancelStripeSubscriptionAsync(previousStripeSubscriptionId, cancellationToken))
            {
                _logger.LogWarning(
                    "Could not cancel the previous Stripe subscription {StripeSubscriptionId} before replacing local subscription {StudentSubscriptionId}.",
                    previousStripeSubscriptionId,
                    currentSubscription.StudentSubscriptionId);
            }

            // StudentSubscription is modeled as one row per student, so replace the
            // current entitlement in place while recording the replacement in Notes.
            currentSubscription.PlanId = transaction.PlanId;
            currentSubscription.SubscriptionStatus = "active";
            currentSubscription.StartedAt = now;
            currentSubscription.PurchasedAt = now;
            currentSubscription.ExpiresAt = now.AddMonths(1);
            currentSubscription.CanceledAt = null;
            currentSubscription.StripeSubscriptionId = stripeSubscriptionId;
            currentSubscription.AutoRenew = false;
            currentSubscription.GrantedBy = null;
            currentSubscription.Notes = $"Replaced the previous exhausted subscription from Stripe test checkout {checkoutId}.";
        }

        transaction.PaymentStatus = "paid";
        transaction.ConfirmedAt = CreateDatabaseTimestamp();

        await _dbContext.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
    }

    private async Task<bool> CancelStripeSubscriptionAsync(
        string? stripeSubscriptionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stripeSubscriptionId))
        {
            return true;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"subscriptions/{Uri.EscapeDataString(stripeSubscriptionId.Trim())}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.SecretKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return true;
        }

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Stripe cancellation returned status {StatusCode} for subscription {StripeSubscriptionId}: {Payload}",
            response.StatusCode,
            stripeSubscriptionId,
            responseText);
        return false;
    }

    private static string? ExtractStripeSubscriptionId(string? rawResponseJson)
    {
        if (string.IsNullOrWhiteSpace(rawResponseJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<StripeCheckoutSessionResponse>(rawResponseJson, SerializerOptions)?.SubscriptionId;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void ValidateOptions()
    {
        if (!HasConfiguredSecretKey())
        {
            throw new InvalidOperationException(BuildMissingSecretKeyMessage());
        }
    }

    private bool HasConfiguredSecretKey()
    {
        return !string.IsNullOrWhiteSpace(_options.SecretKey);
    }

    private static string BuildMissingSecretKeyMessage()
    {
        return "Stripe Sandbox is not configured. Set Stripe:SecretKey to a Stripe test secret key (sk_test_...) in appsettings.json, user secrets, or the Stripe__SecretKey environment variable.";
    }

    private string BuildAbsoluteUrl(string path, string? query = null)
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        var origin = request is null
            ? _options.PublicBaseUrl.TrimEnd('/')
            : $"{request.Scheme}://{request.Host.Value}";
        var baseUrl = $"{origin.TrimEnd('/')}/{path.TrimStart('/')}";
        return string.IsNullOrWhiteSpace(query) ? baseUrl : $"{baseUrl}?{query}";
    }

    private static DateTime CreateDatabaseTimestamp()
    {
        return DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
    }

    private static string BuildCreateCheckoutFailureMessage(System.Net.HttpStatusCode statusCode, string responseText)
    {
        var providerDetail = TryExtractProviderDetail(responseText);

        return statusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => "Stripe Sandbox rejected the secret key with 401 Unauthorized. Generate a Stripe test secret key and update Stripe:SecretKey in appsettings.json.",
            System.Net.HttpStatusCode.Forbidden => "Stripe Sandbox rejected this request with 403 Forbidden. Verify the Stripe test key has permission to create checkout sessions.",
            System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.UnprocessableEntity => string.IsNullOrWhiteSpace(providerDetail)
                ? "Stripe Sandbox rejected the checkout payload. Verify the configured Stripe price ID and checkout settings."
                : $"Stripe Sandbox rejected the checkout payload: {providerDetail}",
            _ => string.IsNullOrWhiteSpace(providerDetail)
                ? $"Unable to initialize Stripe checkout right now. Provider status: {(int)statusCode}."
                : $"Unable to initialize Stripe checkout right now. Provider status: {(int)statusCode}. Detail: {providerDetail}"
        };
    }

    private static string? TryExtractProviderDetail(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (document.RootElement.TryGetProperty("error", out var errorElement))
                {
                    if (errorElement.ValueKind == JsonValueKind.Object &&
                        errorElement.TryGetProperty("message", out var messageElement) &&
                        messageElement.ValueKind == JsonValueKind.String)
                    {
                        return messageElement.GetString();
                    }

                    if (errorElement.ValueKind == JsonValueKind.String)
                    {
                        return errorElement.GetString();
                    }
                }

                if (document.RootElement.TryGetProperty("detail", out var detailElement))
                {
                    return detailElement.ValueKind switch
                    {
                        JsonValueKind.String => detailElement.GetString(),
                        JsonValueKind.Array => string.Join("; ", detailElement.EnumerateArray().Select(FormatValidationItem)),
                        _ => detailElement.ToString()
                    };
                }
            }
        }
        catch (JsonException)
        {
        }

        return responseText.Trim();
    }

    private async Task<string?> GetSubscriptionStatusAsync(string? subscriptionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            return null;
        }

        var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"subscriptions/{subscriptionId.Trim()}");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.SecretKey);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Stripe get subscription returned non-success status {StatusCode}: {Payload}", response.StatusCode, responseText);
            return null;
        }

        var subscription = JsonSerializer.Deserialize<StripeSubscriptionResponse>(responseText, SerializerOptions);
        return subscription?.Status?.Trim().ToLowerInvariant();
    }

    private async Task<string?> ResolveOrCreateProductAsync(
        Guid planId,
        string planCode,
        string planName,
        string? description,
        string? existingStripePriceId,
        CancellationToken cancellationToken)
    {
        var existingPrice = await TryGetPriceAsync(existingStripePriceId, cancellationToken);
        var existingProductId = existingPrice?.ProductId;
        if (!string.IsNullOrWhiteSpace(existingProductId))
        {
            var updatedProduct = await UpsertProductAsync(existingProductId, planId, planCode, planName, description, cancellationToken);
            if (!string.IsNullOrWhiteSpace(updatedProduct))
            {
                return updatedProduct;
            }
        }

        return await CreateProductAsync(planId, planCode, planName, description, cancellationToken);
    }

    private async Task<StripePriceResponse?> TryGetPriceAsync(string? stripePriceId, CancellationToken cancellationToken)
    {
        if (!IsValidStripePriceId(stripePriceId))
        {
            return null;
        }

        using var response = await SendStripeRequestAsync(HttpMethod.Get, $"prices/{stripePriceId!.Trim()}", null, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Stripe get price returned non-success status {StatusCode}: {Payload}", response.StatusCode, responseText);
            return null;
        }

        return JsonSerializer.Deserialize<StripePriceResponse>(responseText, SerializerOptions);
    }

    private async Task<string?> UpsertProductAsync(
        string productId,
        Guid planId,
        string planCode,
        string planName,
        string? description,
        CancellationToken cancellationToken)
    {
        var parameters = BuildProductParameters(planId, planCode, planName, description);
        using var response = await SendStripeFormAsync(HttpMethod.Post, $"products/{productId}", parameters, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Stripe update product returned non-success status {StatusCode}: {Payload}", response.StatusCode, responseText);
            return null;
        }

        var product = JsonSerializer.Deserialize<StripeProductResponse>(responseText, SerializerOptions);
        return product?.Id;
    }

    private async Task<string?> CreateProductAsync(
        Guid planId,
        string planCode,
        string planName,
        string? description,
        CancellationToken cancellationToken)
    {
        var parameters = BuildProductParameters(planId, planCode, planName, description);
        using var response = await SendStripeFormAsync(HttpMethod.Post, "products", parameters, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Stripe create product returned non-success status {StatusCode}: {Payload}", response.StatusCode, responseText);
            return null;
        }

        var product = JsonSerializer.Deserialize<StripeProductResponse>(responseText, SerializerOptions);
        return product?.Id;
    }

    private List<KeyValuePair<string, string>> BuildProductParameters(
        Guid planId,
        string planCode,
        string planName,
        string? description)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("name", planName),
            new("metadata[localPlanId]", planId.ToString("N")),
            new("metadata[localPlanCode]", planCode)
        };

        if (!string.IsNullOrWhiteSpace(description))
        {
            parameters.Add(new("description", description.Trim()));
        }

        return parameters;
    }

    private async Task<HttpResponseMessage> SendStripeFormAsync(
        HttpMethod method,
        string path,
        IEnumerable<KeyValuePair<string, string>> parameters,
        CancellationToken cancellationToken)
    {
        return await SendStripeRequestAsync(method, path, new FormUrlEncodedContent(parameters), cancellationToken);
    }

    private async Task<HttpResponseMessage> SendStripeRequestAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        var httpRequest = new HttpRequestMessage(method, path)
        {
            Content = content
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.SecretKey);

        return await _httpClient.SendAsync(httpRequest, cancellationToken);
    }

    private static string SerializeFormPayload(IEnumerable<KeyValuePair<string, string>> parameters)
    {
        return JsonSerializer.Serialize(
            parameters.Select(item => new Dictionary<string, string>
            {
                ["key"] = item.Key,
                ["value"] = item.Value
            }),
            SerializerOptions);
    }

    private static bool IsValidStripePriceId(string? value)
    {
        var normalizedValue = value?.Trim();
        return !string.IsNullOrWhiteSpace(normalizedValue)
            && normalizedValue.StartsWith("price_", StringComparison.OrdinalIgnoreCase)
            && normalizedValue.Length > "price_".Length;
    }

    private static bool TryConvertAmountToStripeUnits(decimal monthlyPrice, out long unitAmount)
    {
        if (monthlyPrice < 0 || monthlyPrice != decimal.Truncate(monthlyPrice))
        {
            unitAmount = 0;
            return false;
        }

        if (monthlyPrice > long.MaxValue)
        {
            unitAmount = 0;
            return false;
        }

        unitAmount = decimal.ToInt64(monthlyPrice);
        return true;
    }

    private static string FormatValidationItem(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return element.ToString();
        }

        var location = element.TryGetProperty("param", out var paramElement) && paramElement.ValueKind == JsonValueKind.String
            ? paramElement.GetString()
            : "payload";
        var message = element.TryGetProperty("message", out var msgElement)
            ? msgElement.ToString()
            : element.ToString();

        return $"{location}: {message}";
    }

    private sealed record StripeCheckoutSessionResponse(
        [property: JsonPropertyName("id")]
        string? Id,
        [property: JsonPropertyName("url")]
        string? Url,
        [property: JsonPropertyName("payment_status")]
        string? PaymentStatus,
        [property: JsonPropertyName("subscription")]
        JsonElement Subscription,
        [property: JsonPropertyName("status")]
        string? Status)
    {
        public string? SubscriptionId =>
            Subscription.ValueKind switch
            {
                JsonValueKind.String => Subscription.GetString(),
                JsonValueKind.Object when Subscription.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String => idElement.GetString(),
                _ => null
            };
    }

    private sealed record StripeSubscriptionResponse(
        [property: JsonPropertyName("status")]
        string? Status);

    private sealed record StripePriceResponse(
        [property: JsonPropertyName("id")]
        string? Id,
        [property: JsonPropertyName("product")]
        JsonElement Product,
        [property: JsonPropertyName("unit_amount")]
        long? UnitAmount,
        [property: JsonPropertyName("currency")]
        string? Currency,
        [property: JsonPropertyName("recurring")]
        JsonElement Recurring)
    {
        public string? ProductId =>
            Product.ValueKind switch
            {
                JsonValueKind.String => Product.GetString(),
                JsonValueKind.Object when Product.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String => idElement.GetString(),
                _ => null
            };

        public string? RecurringInterval =>
            Recurring.ValueKind == JsonValueKind.Object &&
            Recurring.TryGetProperty("interval", out var intervalElement) &&
            intervalElement.ValueKind == JsonValueKind.String
                ? intervalElement.GetString()
                : null;
    }

    private sealed record StripeProductResponse(
        [property: JsonPropertyName("id")]
        string? Id);
}
