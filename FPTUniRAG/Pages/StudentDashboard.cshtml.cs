using FPTUniRAG.BusinessLayer.Rag.Chat;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages;

public class StudentDashboardModel : PageModel
{
    private readonly IStudentChatService _studentChatService;

    public StudentDashboardModel(IStudentChatService studentChatService)
    {
        _studentChatService = studentChatService;
    }

    public StudentChatPageDto Dashboard { get; private set; } = new(string.Empty, [], []);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var userId = GetRequiredUserId();
        var studentName = User.FindFirstValue(ClaimTypes.Name) ?? "Student";
        Dashboard = await _studentChatService.GetDashboardAsync(userId, studentName, cancellationToken);
    }

    private Guid GetRequiredUserId()
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(rawUserId, out var userId)
            ? userId
            : throw new InvalidOperationException("Authenticated user identifier is missing.");
    }
}
