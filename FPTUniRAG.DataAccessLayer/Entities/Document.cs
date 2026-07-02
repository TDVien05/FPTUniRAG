using System;
using System.Collections.Generic;

namespace FPTUniRAG.DataAccessLayer.Entities;

public partial class Document
{
    public Guid DocumentId { get; set; }

    public Guid ChapterId { get; set; }

    public string Title { get; set; } = null!;

    public string FileUrl { get; set; } = null!;

    public string? FileType { get; set; }

    public string ChunkingStrategy { get; set; } = null!;

    public int ChunkSize { get; set; }

    public int ChunkOverlap { get; set; }

    public Guid? UploadedBy { get; set; }

    public Guid? UploadedTeacher { get; set; }

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    public Guid SubjectId { get; set; }

    public virtual Chapter Chapter { get; set; } = null!;

    public virtual ICollection<Chunk> Chunks { get; set; } = new List<Chunk>();

    public virtual ICollection<ProcessingJob> ProcessingJobs { get; set; } = new List<ProcessingJob>();

    public virtual Subject Subject { get; set; } = null!;

    public virtual User? UploadedByNavigation { get; set; }

    public virtual Teacher? UploadedTeacherNavigation { get; set; }
}
