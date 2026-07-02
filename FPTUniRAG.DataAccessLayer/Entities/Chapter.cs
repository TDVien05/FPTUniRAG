using System;
using System.Collections.Generic;

namespace FPTUniRAG.DataAccessLayer.Entities;

public partial class Chapter
{
    public Guid ChapterId { get; set; }

    public Guid SubjectId { get; set; }

    public string ChapterTitle { get; set; } = null!;

    public int ChapterOrder { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Document? Document { get; set; }

    public virtual Subject Subject { get; set; } = null!;

    public virtual ICollection<TestQuestion> TestQuestions { get; set; } = new List<TestQuestion>();
}
