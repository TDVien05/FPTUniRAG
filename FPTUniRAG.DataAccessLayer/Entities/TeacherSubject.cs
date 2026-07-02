using System;
using System.Collections.Generic;

namespace FPTUniRAG.DataAccessLayer.Entities;

public partial class TeacherSubject
{
    public Guid TeacherSubjectId { get; set; }

    public Guid TeacherId { get; set; }

    public Guid SubjectId { get; set; }

    public bool IsHeadOfDepartment { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Subject Subject { get; set; } = null!;

    public virtual Teacher Teacher { get; set; } = null!;
}
