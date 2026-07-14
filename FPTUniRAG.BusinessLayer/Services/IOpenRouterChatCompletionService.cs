namespace FPTUniRAG.BusinessLayer.Services;

public interface IOpenRouterChatCompletionService
{
    Task<OpenRouterChatResult> StreamCompletionAsync(
        IReadOnlyList<OpenRouterChatMessage> messages,
        Func<string, CancellationToken, Task> onDelta,
        CancellationToken cancellationToken = default);
}
