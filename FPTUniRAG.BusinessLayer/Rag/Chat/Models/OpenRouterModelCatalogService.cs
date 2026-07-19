using FPTUniRAG.BusinessLayer.Rag.Configuration;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FPTUniRAG.BusinessLayer.Rag.Chat.Models;

public sealed class OpenRouterModelCatalogService : IOpenRouterModelCatalogService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);
    private static readonly SemaphoreSlim CacheLock = new(1, 1);

    // The catalog holds 300+ entries and rarely changes, so it is cached process-wide
    // rather than refetched on every admin keystroke.
    private static IReadOnlyDictionary<string, OpenRouterCatalogModel>? _cache;
    private static DateTime _cachedAtUtc;

    private readonly HttpClient _httpClient;
    private readonly RagIngestionOptions _options;
    private readonly ILogger<OpenRouterModelCatalogService> _logger;

    public OpenRouterModelCatalogService(
        HttpClient httpClient,
        IOptions<RagIngestionOptions> options,
        ILogger<OpenRouterModelCatalogService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<OpenRouterCatalogModel?> FindModelAsync(string slug, CancellationToken cancellationToken = default)
    {
        var normalized = slug.Trim();
        if (normalized.Length == 0)
        {
            return null;
        }

        var catalog = await GetCatalogAsync(cancellationToken);
        return catalog.TryGetValue(normalized, out var model) ? model : null;
    }

    private async Task<IReadOnlyDictionary<string, OpenRouterCatalogModel>> GetCatalogAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null && DateTime.UtcNow - _cachedAtUtc < CacheLifetime)
        {
            return _cache;
        }

        await CacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache is not null && DateTime.UtcNow - _cachedAtUtc < CacheLifetime)
            {
                return _cache;
            }

            var catalog = await FetchCatalogAsync(cancellationToken);
            _cache = catalog;
            _cachedAtUtc = DateTime.UtcNow;
            return catalog;
        }
        finally
        {
            CacheLock.Release();
        }
    }

    private async Task<IReadOnlyDictionary<string, OpenRouterCatalogModel>> FetchCatalogAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "models");
        if (!string.IsNullOrWhiteSpace(_options.OpenRouter.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.OpenRouter.ApiKey);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("OpenRouter model catalog request failed with status {Status}: {Body}", (int)response.StatusCode, body);
            throw new InvalidOperationException($"OpenRouter model catalog request failed with status {(int)response.StatusCode}.");
        }

        var payload = await response.Content.ReadFromJsonAsync<OpenRouterModelListResponse>(SerializerOptions, cancellationToken);
        var models = payload?.Data ?? [];

        return models
            .Where(model => !string.IsNullOrWhiteSpace(model.Id))
            .GroupBy(model => model.Id!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new OpenRouterCatalogModel(group.Key, group.First().Name, group.First().ContextLength),
                StringComparer.OrdinalIgnoreCase);
    }

    private sealed record OpenRouterModelListResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<OpenRouterModelListEntry>? Data);

    private sealed record OpenRouterModelListEntry(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("context_length")] int? ContextLength);
}
