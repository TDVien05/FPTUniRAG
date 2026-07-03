using System.ComponentModel.DataAnnotations;

namespace FPTUniRAG.Pages.Subjects;

public sealed class SubjectInputModel
{
    [Required(ErrorMessage = "Subject code is required.")]
    [StringLength(50, ErrorMessage = "Subject code cannot exceed 50 characters.")]
    public string SubjectCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Subject name is required.")]
    [StringLength(255, ErrorMessage = "Subject name cannot exceed 255 characters.")]
    public string SubjectName { get; set; } = string.Empty;

    public string? Description { get; set; }
}
