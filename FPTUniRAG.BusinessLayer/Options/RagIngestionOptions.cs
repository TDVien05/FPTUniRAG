namespace FPTUniRAG.BusinessLayer.Options;

public sealed class RagIngestionOptions
{
    public int FixedChunkOverlap { get; set; } = 120;

    public string StorageRoot { get; set; } = "App_Data/teacher-uploads";

    public string[] AllowedFileTypes { get; set; } = [".docx", ".txt", ".md", ".pdf"];

    public OpenRouterIngestionOptions OpenRouter { get; set; } = new();

    public PostgresVectorStorageOptions PostgresVector { get; set; } = new();

    public SemanticChunkingOptions Semantic { get; set; } = new();

    public TesseractOcrOptions Tesseract { get; set; } = new();
}

public sealed class OpenRouterIngestionOptions
{
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

    public string ApiKey { get; set; } = string.Empty;

    public string EmbeddingModel { get; set; } = string.Empty;

    public string ChatModel { get; set; } = string.Empty;

    public int EmbeddingDimensions { get; set; } = 1536;

    public int MaxCompletionTokens { get; set; } = 800;

    public float Temperature { get; set; } = 0.2f;
}

public sealed class PostgresVectorStorageOptions
{
    public string TableName { get; set; } = "chunk_embeddings";

    public int BatchSize { get; set; } = 8;
}

public sealed class SemanticChunkingOptions
{
    public int MaxChunkSize { get; set; } = 1200;

    public int MinChunkSize { get; set; } = 300;
}

public sealed class TesseractOcrOptions
{
    public bool Enabled { get; set; } = true;

    public string ExecutablePath { get; set; } = "tesseract";

    public string Language { get; set; } = "eng";

    public string LanguageDataPath { get; set; } = string.Empty;
}
