using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FPTUniRAG.Options;
using Microsoft.Extensions.Options;

namespace FPTUniRAG.Services;

public sealed class QdrantChunkEmbeddingStore : IChunkEmbeddingStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly RagIngestionOptions _options;

    public QdrantChunkEmbeddingStore(HttpClient httpClient, IOptions<RagIngestionOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task SaveEmbeddingsAsync(
        IReadOnlyList<(Guid ChunkId, float[] Embedding)> embeddings,
        CancellationToken cancellationToken = default)
    {
        if (embeddings.Count == 0)
        {
            return;
        }

        ValidateOptions();

        await EnsureCollectionAsync(embeddings[0].Embedding.Length, cancellationToken);

        var batchSize = Math.Max(1, _options.Qdrant.BatchSize);
        foreach (var batch in embeddings.Chunk(batchSize))
        {
            var points = batch
                .Select(item => new QdrantPoint(
                    item.ChunkId.ToString(),
                    item.Embedding,
                    new Dictionary<string, object?>
                    {
                        ["chunk_id"] = item.ChunkId,
                        ["embedding_model"] = _options.OpenRouter.EmbeddingModel,
                        ["dimensions"] = item.Embedding.Length,
                        ["created_at_utc"] = DateTime.UtcNow
                    }))
                .ToList();

            using var request = new HttpRequestMessage(
                HttpMethod.Put,
                $"collections/{Uri.EscapeDataString(_options.Qdrant.CollectionName)}/points?wait=true")
            {
                Content = JsonContent.Create(new QdrantUpsertPointsRequest(points), options: SerializerOptions)
            };
            request.Version = HttpVersion.Version11;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;

            ApplyApiKey(request);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"Qdrant point upsert failed with status {(int)response.StatusCode}: {error}");
            }
        }
    }

    private async Task EnsureCollectionAsync(int vectorSize, CancellationToken cancellationToken)
    {
        var collectionName = Uri.EscapeDataString(_options.Qdrant.CollectionName);
        using var getRequest = new HttpRequestMessage(HttpMethod.Get, $"collections/{collectionName}");
        getRequest.Version = HttpVersion.Version11;
        getRequest.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
        ApplyApiKey(getRequest);

        using var getResponse = await _httpClient.SendAsync(getRequest, cancellationToken);
        if (getResponse.StatusCode == HttpStatusCode.NotFound)
        {
            using var createRequest = new HttpRequestMessage(HttpMethod.Put, $"collections/{collectionName}")
            {
                Content = JsonContent.Create(
                    new QdrantCreateCollectionRequest(new QdrantVectorConfig(vectorSize, _options.Qdrant.Distance)),
                    options: SerializerOptions)
            };
            createRequest.Version = HttpVersion.Version11;
            createRequest.VersionPolicy = HttpVersionPolicy.RequestVersionExact;

            ApplyApiKey(createRequest);

            using var createResponse = await _httpClient.SendAsync(createRequest, cancellationToken);
            if (!createResponse.IsSuccessStatusCode)
            {
                var error = await createResponse.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"Qdrant collection creation failed with status {(int)createResponse.StatusCode}: {error}");
            }

            return;
        }

        if (!getResponse.IsSuccessStatusCode)
        {
            var error = await getResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Qdrant collection lookup failed with status {(int)getResponse.StatusCode}: {error}");
        }

        var collection = await getResponse.Content.ReadFromJsonAsync<QdrantCollectionResponse>(SerializerOptions, cancellationToken);
        var currentVectorSize = collection?.Result?.Config?.Params?.Vectors?.Size;
        if (currentVectorSize.HasValue && currentVectorSize.Value != vectorSize)
        {
            throw new InvalidOperationException(
                $"Qdrant collection '{_options.Qdrant.CollectionName}' expects vector size {currentVectorSize.Value}, but the current embedding model produced {vectorSize} dimensions.");
        }
    }

    private void ApplyApiKey(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_options.Qdrant.ApiKey))
        {
            request.Headers.Add("api-key", _options.Qdrant.ApiKey);
        }
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.Qdrant.BaseUrl))
        {
            throw new InvalidOperationException("RagIngestion:Qdrant:BaseUrl is missing in appsettings.json.");
        }

        if (string.IsNullOrWhiteSpace(_options.Qdrant.CollectionName))
        {
            throw new InvalidOperationException("RagIngestion:Qdrant:CollectionName is missing in appsettings.json.");
        }

        if (string.IsNullOrWhiteSpace(_options.Qdrant.Distance))
        {
            throw new InvalidOperationException("RagIngestion:Qdrant:Distance is missing in appsettings.json.");
        }

        if (_options.Qdrant.BatchSize <= 0)
        {
            throw new InvalidOperationException("RagIngestion:Qdrant:BatchSize must be greater than zero.");
        }
    }

    private sealed record QdrantUpsertPointsRequest(
        [property: JsonPropertyName("points")] IReadOnlyList<QdrantPoint> Points);

    private sealed record QdrantPoint(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("vector")] float[] Vector,
        [property: JsonPropertyName("payload")] Dictionary<string, object?> Payload);

    private sealed record QdrantCreateCollectionRequest(
        [property: JsonPropertyName("vectors")] QdrantVectorConfig Vectors);

    private sealed record QdrantVectorConfig(
        [property: JsonPropertyName("size")] int Size,
        [property: JsonPropertyName("distance")] string Distance);

    private sealed record QdrantCollectionResponse(
        [property: JsonPropertyName("result")] QdrantCollectionResult? Result);

    private sealed record QdrantCollectionResult(
        [property: JsonPropertyName("config")] QdrantCollectionConfig? Config);

    private sealed record QdrantCollectionConfig(
        [property: JsonPropertyName("params")] QdrantCollectionParams? Params);

    private sealed record QdrantCollectionParams(
        [property: JsonPropertyName("vectors")] QdrantExistingVectorConfig? Vectors);

    private sealed record QdrantExistingVectorConfig(
        [property: JsonPropertyName("size")] int? Size);
}
