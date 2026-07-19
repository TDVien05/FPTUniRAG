namespace FPTUniRAG.BusinessLayer.Rag.Chat;

public interface IOpenRouterChatCompletionService
{
    /// <param name="model">
    /// OpenRouter slug to call. When null the configured RagIngestion:OpenRouter:ChatModel is used.
    /// The benchmark passes the model under test here; student chat passes the admin-selected model.
    /// </param>
    Task<OpenRouterChatResult> StreamCompletionAsync(
        IReadOnlyList<OpenRouterChatMessage> messages,
        Func<string, CancellationToken, Task> onDelta,
        string? model = null,
        CancellationToken cancellationToken = default);
}
