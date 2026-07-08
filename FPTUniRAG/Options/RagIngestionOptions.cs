namespace FPTUniRAG.Options;

public sealed class RagIngestionOptions
{
    public int FixedChunkOverlap { get; set; } = 120;

    public string StorageRoot { get; set; } = "App_Data/teacher-uploads";

    public string[] AllowedFileTypes { get; set; } = [".docx", ".txt", ".md", ".pdf"];

    public OpenRouterIngestionOptions OpenRouter { get; set; } = new();

    public QdrantIngestionOptions Qdrant { get; set; } = new();

    public SemanticChunkingOptions Semantic { get; set; } = new();

    public TesseractOcrOptions Tesseract { get; set; } = new();
}

public sealed class OpenRouterIngestionOptions
{
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

    public string ApiKey { get; set; } = string.Empty;

    public string EmbeddingModel { get; set; } = string.Empty;

    public int EmbeddingDimensions { get; set; } = 1536;
}

public sealed class QdrantIngestionOptions
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:6333";

    public string ApiKey { get; set; } = string.Empty;

    public string CollectionName { get; set; } = "teacher_document_chunks";

    public string Distance { get; set; } = "Cosine";

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
