namespace FPTUniRAG.BusinessLayer.Rag.Chat.Models;

public interface IOpenRouterModelCatalogService
{
    /// <summary>
    /// Looks a slug up in OpenRouter's catalog. Returns null when the slug does not exist,
    /// which is how an admin typo is rejected before it can break student chat.
    /// </summary>
    Task<OpenRouterCatalogModel?> FindModelAsync(string slug, CancellationToken cancellationToken = default);
}

public sealed record OpenRouterCatalogModel(string Id, string? Name, int? ContextLength);
