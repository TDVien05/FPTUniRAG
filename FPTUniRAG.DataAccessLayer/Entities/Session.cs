using System;
using System.Collections.Generic;

namespace FPTUniRAG.DataAccessLayer.Entities;

public partial class Session
{
    public Guid SessionId { get; set; }

    public Guid UserId { get; set; }

    public Guid? SubjectId { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? EndedAt { get; set; }

    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();

    public virtual Subject? Subject { get; set; }

    public virtual ICollection<TokenUsageLog> TokenUsageLogs { get; set; } = new List<TokenUsageLog>();

    public virtual User User { get; set; } = null!;
}
