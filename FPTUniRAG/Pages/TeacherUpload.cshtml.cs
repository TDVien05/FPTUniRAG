using System.Security.Claims;
using FPTUniRAG.BusinessLayer.Subjects;
using FPTUniRAG.BusinessLayer.Options;
using FPTUniRAG.BusinessLayer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace FPTUniRAG.Pages;

[Authorize(Policy = "TeacherOrAdmin")]
public class TeacherUploadModel : PageModel
{
    private readonly ITeacherDocumentWorkflowService _teacherDocumentWorkflowService;
    private readonly IEmbeddingConfigurationService _embeddingConfigurationService;
    private readonly RagIngestionOptions _options;

    public TeacherUploadModel(
        ITeacherDocumentWorkflowService teacherDocumentWorkflowService,
        IOptions<RagIngestionOptions> options,
        IEmbeddingConfigurationService embeddingConfigurationService)
    {
        _teacherDocumentWorkflowService = teacherDocumentWorkflowService;
        _options = options.Value;
        _embeddingConfigurationService = embeddingConfigurationService;
    }

    [BindProperty]
    public TeacherDocumentUploadCommand Input { get; set; } = new();

    [TempData]
    public string? NoticeMessage { get; set; }

    public TeacherUploadContextDto? UploadContext { get; private set; }

    public string ChunkingStrategyLabel => SubjectChunkingStrategies.ToDisplayLabel(UploadContext?.DefaultChunkingStrategy);

    public int FixedChunkSize => UploadContext?.DefaultFixedChunkSize ?? 0;

    public int FixedChunkOverlap => _options.FixedChunkOverlap;

    public int SemanticMaxChunkSize => _options.Semantic.MaxChunkSize;

    public int SemanticMinChunkSize => _options.Semantic.MinChunkSize;

    public EmbeddingConfigurationSnapshot? EmbeddingConfiguration { get; private set; }

    [BindProperty(SupportsGet = true)]
    public Guid? ProcessingDocumentId { get; set; }

    public string EmbeddingModel => EmbeddingConfiguration?.Model ?? _options.OpenRouter.EmbeddingModel;

    public async Task<IActionResult> OnGetAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        var teacherEmail = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(teacherEmail))
        {
            return Challenge();
        }

        UploadContext = await _teacherDocumentWorkflowService.GetUploadContextAsync(
            teacherEmail,
            subjectId,
            cancellationToken);
        EmbeddingConfiguration = await _embeddingConfigurationService.GetCurrentAsync(cancellationToken);

        if (UploadContext is null)
        {
            return Forbid();
        }

        Input.SubjectId = subjectId;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var teacherEmail = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(teacherEmail))
        {
            return Challenge();
        }

        UploadContext = await _teacherDocumentWorkflowService.GetUploadContextAsync(
            teacherEmail,
            Input.SubjectId,
            cancellationToken);

        if (UploadContext is null)
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _teacherDocumentWorkflowService.UploadAsync(
            teacherEmail,
            Input,
            cancellationToken);

        if (!result.Succeeded || !result.DocumentId.HasValue)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return Page();
        }

        NoticeMessage = result.Message;
        ProcessingDocumentId = result.DocumentId.Value;
        EmbeddingConfiguration = await _embeddingConfigurationService.GetCurrentAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnGetStatusAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var teacherEmail = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(teacherEmail))
        {
            return Challenge();
        }

        var status = await _teacherDocumentWorkflowService.GetProcessingStatusAsync(
            teacherEmail,
            documentId,
            cancellationToken);

        return status is null ? NotFound() : new JsonResult(status);
    }
}
