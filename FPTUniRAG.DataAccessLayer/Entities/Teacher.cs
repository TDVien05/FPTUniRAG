using System;
using System.Collections.Generic;

namespace FPTUniRAG.DataAccessLayer.Entities;

public partial class Teacher
{
    public Guid TeacherId { get; set; }

    public string FullName { get; set; } = null!;

    public string? Email { get; set; }

    public string? Department { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();

    public virtual ICollection<TeacherSubject> TeacherSubjects { get; set; } = new List<TeacherSubject>();
}
