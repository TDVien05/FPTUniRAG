using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FPTUniRAG.Options;
using Microsoft.Extensions.Options;

namespace FPTUniRAG.Services;

public sealed class OpenRouterEmbeddingService : IOpenRouterEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly RagIngestionOptions _options;

    public OpenRouterEmbeddingService(HttpClient httpClient, IOptions<RagIngestionOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<float[]>> CreateEmbeddingsAsync(
        IReadOnlyList<string> chunks,
        CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
        {
            return [];
        }

        ValidateConfiguration();

        using var request = new HttpRequestMessage(HttpMethod.Post, "embeddings")
        {
            Content = JsonContent.Create(new OpenRouterEmbeddingRequest(
                _options.OpenRouter.EmbeddingModel,
                chunks,
                _options.OpenRouter.EmbeddingDimensions))
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.OpenRouter.ApiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"OpenRouter embedding request failed with status {(int)response.StatusCode}: {errorBody}");
        }

        var payload = await response.Content.ReadFromJsonAsync<OpenRouterEmbeddingResponse>(cancellationToken: cancellationToken);
        if (payload?.Data is null || payload.Data.Count != chunks.Count)
        {
            throw new InvalidOperationException("OpenRouter returned an invalid embedding payload.");
        }

        return payload.Data
            .OrderBy(item => item.Index)
            .Select(item => item.Embedding.ToArray())
            .ToList();
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.OpenRouter.ApiKey))
        {
            throw new InvalidOperationException("RagIngestion:OpenRouter:ApiKey is missing in appsettings.json.");
        }

        if (string.IsNullOrWhiteSpace(_options.OpenRouter.EmbeddingModel))
        {
            throw new InvalidOperationException("RagIngestion:OpenRouter:EmbeddingModel is missing in appsettings.json.");
        }
    }

    private sealed record OpenRouterEmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input,
        [property: JsonPropertyName("dimensions")] int Dimensions);

    private sealed record OpenRouterEmbeddingResponse(
        [property: JsonPropertyName("data")] List<OpenRouterEmbeddingData> Data);

    private sealed record OpenRouterEmbeddingData(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] List<float> Embedding);
}
