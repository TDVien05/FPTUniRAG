using System;

namespace FPTUniRAG.DataAccessLayer.Entities;

public sealed class ChatModel
{
    public Guid ChatModelId { get; set; }

    public string ModelName { get; set; } = null!;

    public string? DisplayName { get; set; }

    public int? ContextLength { get; set; }

    public bool IsSelected { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }
}
