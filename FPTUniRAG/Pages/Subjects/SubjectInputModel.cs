using FPTUniRAG.BusinessLayer.Subjects;
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

    [Required(ErrorMessage = "Chunking strategy is required.")]
    [RegularExpression("^(fixed|semantic)$", ErrorMessage = "Chunking strategy must be fixed or semantic.")]
    public string DefaultChunkingStrategy { get; set; } = SubjectChunkingStrategies.Fixed;

    [Range(1, int.MaxValue, ErrorMessage = "Fixed chunk size must be greater than zero.")]
    public int DefaultFixedChunkSize { get; set; } = 800;
}
