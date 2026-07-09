using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FPTUniRAG.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FPTUniRAG.Services;

public sealed class OpenRouterChatCompletionService : IOpenRouterChatCompletionService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly RagIngestionOptions _options;
    private readonly ILogger<OpenRouterChatCompletionService> _logger;

    public OpenRouterChatCompletionService(
        HttpClient httpClient,
        IOptions<RagIngestionOptions> options,
        ILogger<OpenRouterChatCompletionService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<OpenRouterChatResult> StreamCompletionAsync(
        IReadOnlyList<OpenRouterChatMessage> messages,
        Func<string, CancellationToken, Task> onDelta,
        CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0)
        {
            throw new InvalidOperationException("OpenRouter chat request requires at least one message.");
        }

        ValidateConfiguration();

        var streamedTextBuilder = new StringBuilder();
        OpenRouterChatCompletionResponse? finalStreamPayload = null;
        string? streamedFinishReason = null;

        using var request = CreateChatRequest(messages, stream: true);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"OpenRouter chat request failed with status {(int)response.StatusCode}: {errorBody}");
        }

        await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
        using (var reader = new StreamReader(stream))
        {
            while (true)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var payloadText = line["data:".Length..].Trim();
                if (string.Equals(payloadText, "[DONE]", StringComparison.Ordinal))
                {
                    break;
                }

                OpenRouterChatCompletionResponse? payload;
                try
                {
                    payload = JsonSerializer.Deserialize<OpenRouterChatCompletionResponse>(payloadText, SerializerOptions);
                }
                catch (JsonException exception)
                {
                    _logger.LogWarning(exception, "Failed to deserialize OpenRouter streaming payload.");
                    continue;
                }

                if (payload is null)
                {
                    continue;
                }

                finalStreamPayload = payload;
                var choice = payload.Choices?.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(choice?.FinishReason))
                {
                    streamedFinishReason = choice.FinishReason;
                }

                var deltaText = ExtractText(choice?.Delta?.Content);
                if (!string.IsNullOrEmpty(deltaText))
                {
                    streamedTextBuilder.Append(deltaText);
                    await onDelta(deltaText, cancellationToken);
                }

                var fullMessageText = ExtractText(choice?.Message?.Content);
                if (!string.IsNullOrEmpty(fullMessageText) && fullMessageText.Length > streamedTextBuilder.Length)
                {
                    var missingSuffix = fullMessageText[streamedTextBuilder.Length..];
                    streamedTextBuilder.Clear();
                    streamedTextBuilder.Append(fullMessageText);

                    if (!string.IsNullOrEmpty(missingSuffix))
                    {
                        await onDelta(missingSuffix, cancellationToken);
                    }
                }
            }
        }

        var streamedText = streamedTextBuilder.ToString().Trim();
        var finalMessageText = ExtractText(finalStreamPayload?.Choices?.FirstOrDefault()?.Message?.Content)?.Trim();
        var canonicalText = SelectCanonicalText(streamedText, finalMessageText);
        var fallbackUsed = false;
        var canonicalFinishReason = streamedFinishReason;
        var usage = finalStreamPayload?.Usage;
        string modelName = finalStreamPayload?.Model?.Trim() ?? _options.OpenRouter.ChatModel;

        if (ShouldFallback(canonicalText, streamedFinishReason))
        {
            _logger.LogWarning(
                "OpenRouter stream looked incomplete. Model={Model} FinishReason={FinishReason} StreamedChars={StreamedChars} CanonicalChars={CanonicalChars}. Triggering non-stream fallback.",
                modelName,
                streamedFinishReason ?? "(null)",
                streamedText.Length,
                canonicalText.Length);

            var fallbackResult = await CompleteNonStreamingAsync(messages, cancellationToken);
            canonicalText = fallbackResult.Content.Trim();
            canonicalFinishReason = fallbackResult.FinishReason;
            usage = fallbackResult.Usage;
            modelName = fallbackResult.ModelName;
            fallbackUsed = true;
        }

        if (string.IsNullOrWhiteSpace(canonicalText))
        {
            throw new InvalidOperationException("OpenRouter returned an empty chat completion payload.");
        }

        _logger.LogInformation(
            "OpenRouter completion finalized. Model={Model} FinishReason={FinishReason} StreamedChars={StreamedChars} FinalChars={FinalChars} FallbackUsed={FallbackUsed}",
            modelName,
            canonicalFinishReason ?? "(null)",
            streamedText.Length,
            canonicalText.Length,
            fallbackUsed);

        return new OpenRouterChatResult(
            modelName,
            canonicalText,
            usage?.PromptTokens ?? 0,
            usage?.CompletionTokens ?? 0,
            usage?.TotalTokens ?? 0,
            canonicalFinishReason,
            streamedText.Length,
            fallbackUsed);
    }

    private async Task<OpenRouterNonStreamingResult> CompleteNonStreamingAsync(
        IReadOnlyList<OpenRouterChatMessage> messages,
        CancellationToken cancellationToken)
    {
        using var request = CreateChatRequest(messages, stream: false);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"OpenRouter non-stream fallback failed with status {(int)response.StatusCode}: {errorBody}");
        }

        var payload = await response.Content.ReadFromJsonAsync<OpenRouterChatCompletionResponse>(SerializerOptions, cancellationToken);
        var choice = payload?.Choices?.FirstOrDefault();
        var content = ExtractText(choice?.Message?.Content)?.Trim()
            ?? ExtractText(choice?.Delta?.Content)?.Trim()
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("OpenRouter non-stream fallback returned empty content.");
        }

        return new OpenRouterNonStreamingResult(
            payload?.Model?.Trim() ?? _options.OpenRouter.ChatModel,
            content,
            choice?.FinishReason,
            payload?.Usage);
    }

    private HttpRequestMessage CreateChatRequest(
        IReadOnlyList<OpenRouterChatMessage> messages,
        bool stream)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = System.Net.Http.Json.JsonContent.Create(
                new OpenRouterChatCompletionRequest(
                    _options.OpenRouter.ChatModel,
                    messages.Select(message => new OpenRouterChatCompletionRequestMessage(message.Role, message.Content)).ToList(),
                    _options.OpenRouter.MaxCompletionTokens,
                    _options.OpenRouter.Temperature,
                    stream),
                options: SerializerOptions)
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.OpenRouter.ApiKey);
        return request;
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.OpenRouter.ApiKey))
        {
            throw new InvalidOperationException("RagIngestion:OpenRouter:ApiKey is missing in appsettings.json.");
        }

        if (string.IsNullOrWhiteSpace(_options.OpenRouter.ChatModel))
        {
            throw new InvalidOperationException("RagIngestion:OpenRouter:ChatModel is missing in appsettings.json.");
        }
    }

    private static string SelectCanonicalText(string streamedText, string? finalMessageText)
    {
        if (!string.IsNullOrWhiteSpace(finalMessageText) && finalMessageText.Length >= streamedText.Length)
        {
            return finalMessageText;
        }

        return streamedText;
    }

    private static bool ShouldFallback(string content, string? finishReason)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(finishReason)
            && !string.Equals(finishReason, "stop", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (content.Length >= 80 && LooksAbruptlyCut(content))
        {
            return true;
        }

        return false;
    }

    private static bool LooksAbruptlyCut(string content)
    {
        var trimmed = content.TrimEnd();
        if (trimmed.Length == 0)
        {
            return true;
        }

        var lastCharacter = trimmed[^1];
        if (".!?)]}\"'`".Contains(lastCharacter))
        {
            return false;
        }

        if (trimmed.EndsWith("```", StringComparison.Ordinal))
        {
            return false;
        }

        return char.IsLetterOrDigit(lastCharacter);
    }

    private static string? ExtractText(JsonElement? content)
    {
        if (content is null || content.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        return ExtractText(content.Value);
    }

    private static string? ExtractText(JsonElement content)
    {
        switch (content.ValueKind)
        {
            case JsonValueKind.String:
                return content.GetString();

            case JsonValueKind.Array:
            {
                var builder = new StringBuilder();
                foreach (var item in content.EnumerateArray())
                {
                    var piece = ExtractText(item);
                    if (!string.IsNullOrEmpty(piece))
                    {
                        builder.Append(piece);
                    }
                }

                return builder.Length == 0 ? null : builder.ToString();
            }

            case JsonValueKind.Object:
            {
                if (content.TryGetProperty("text", out var textProperty))
                {
                    return ExtractText(textProperty);
                }

                if (content.TryGetProperty("content", out var contentProperty))
                {
                    return ExtractText(contentProperty);
                }

                if (content.TryGetProperty("value", out var valueProperty))
                {
                    return ExtractText(valueProperty);
                }

                return null;
            }

            default:
                return null;
        }
    }

    private sealed record OpenRouterChatCompletionRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<OpenRouterChatCompletionRequestMessage> Messages,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("temperature")] float Temperature,
        [property: JsonPropertyName("stream")] bool Stream);

    private sealed record OpenRouterChatCompletionRequestMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record OpenRouterChatCompletionResponse(
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("choices")] List<OpenRouterChatCompletionChoice>? Choices,
        [property: JsonPropertyName("usage")] OpenRouterChatCompletionUsage? Usage);

    private sealed record OpenRouterChatCompletionChoice(
        [property: JsonPropertyName("message")] OpenRouterChatCompletionResponseMessage? Message,
        [property: JsonPropertyName("delta")] OpenRouterChatCompletionResponseMessage? Delta,
        [property: JsonPropertyName("finish_reason")] string? FinishReason);

    private sealed record OpenRouterChatCompletionResponseMessage(
        [property: JsonPropertyName("content")] JsonElement? Content);

    private sealed record OpenRouterChatCompletionUsage(
        [property: JsonPropertyName("prompt_tokens")] long PromptTokens,
        [property: JsonPropertyName("completion_tokens")] long CompletionTokens,
        [property: JsonPropertyName("total_tokens")] long TotalTokens);

    private sealed record OpenRouterNonStreamingResult(
        string ModelName,
        string Content,
        string? FinishReason,
        OpenRouterChatCompletionUsage? Usage);
}
