using System.Security.Claims;
using FPTUniRAG.BusinessLayer.Common;
using FPTUniRAG.BusinessLayer.Subjects;
using FPTUniRAG.Pages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Xunit;

namespace FPTUniRAG.Tests.TeacherDocuments;

public sealed class TeacherDocumentsModelTests
{
    [Fact]
    public async Task OnGetAsync_SelectsFirstManagedSubjectByDefault()
    {
        var first = CreateSubject("PRN222", "Web Application Development");
        var second = CreateSubject("SWR302", "Software Requirements");
        var model = CreateModel([first, second]);

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(first.SubjectId, model.SubjectId);
        Assert.Equal(first, model.SelectedSubject);
        Assert.Equal(first.Documents, model.VisibleDocuments);
    }

    [Fact]
    public async Task OnGetAsync_FallsBackWhenRequestedSubjectIsNotManaged()
    {
        var first = CreateSubject("PRN222", "Web Application Development");
        var model = CreateModel([first]);
        model.SubjectId = Guid.NewGuid();

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(first.SubjectId, model.SubjectId);
        Assert.Equal(first, model.SelectedSubject);
    }

    [Fact]
    public async Task OnGetAsync_FiltersDocumentsOnlyInsideSelectedSubject()
    {
        var selected = CreateSubject(
            "PRN222",
            "Web Application Development",
            CreateDocument("Chapter 1", "Introduction.pdf"),
            CreateDocument("Chapter 2", "Dependency Injection.pdf"));
        var other = CreateSubject(
            "SWR302",
            "Software Requirements",
            CreateDocument("Target chapter", "Target document.pdf"));
        var model = CreateModel([selected, other]);
        model.SubjectId = selected.SubjectId;
        model.Query = "Dependency";

        await model.OnGetAsync(CancellationToken.None);

        var visible = Assert.Single(model.VisibleDocuments);
        Assert.Equal("Dependency Injection.pdf", visible.DocumentTitle);
        Assert.Equal(2, model.ManagedSubjects.Count);
    }

    private static TeacherDocumentsModel CreateModel(IReadOnlyList<TeacherDocumentManagementItemDto> subjects)
    {
        var model = new TeacherDocumentsModel(new StubSubjectManagementService(subjects), null!);
        model.PageContext = new PageContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Email, "teacher@fpt.edu.vn")],
                    "Test"))
            }
        };
        return model;
    }

    private static TeacherDocumentManagementItemDto CreateSubject(
        string code,
        string name,
        params TeacherSubjectDocumentDto[] documents)
    {
        var subjectId = Guid.NewGuid();
        return new TeacherDocumentManagementItemDto(
            subjectId,
            code,
            name,
            documents.Length,
            documents.LastOrDefault()?.CreatedAt,
            documents.LastOrDefault()?.DocumentId,
            documents.LastOrDefault()?.DocumentTitle,
            documents.LastOrDefault()?.Status,
            documents,
            true);
    }

    private static TeacherSubjectDocumentDto CreateDocument(string chapterTitle, string documentTitle) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            chapterTitle,
            documentTitle,
            "completed",
            null,
            3,
            DateTime.UtcNow);

    private sealed class StubSubjectManagementService(IReadOnlyList<TeacherDocumentManagementItemDto> subjects)
        : ISubjectManagementService
    {
        public Task<IReadOnlyList<TeacherDocumentManagementItemDto>> GetDocumentManagementItemsForTeacherAsync(
            string teacherEmail,
            CancellationToken cancellationToken = default) => Task.FromResult(subjects);

        public Task<IReadOnlyList<SubjectListItemDto>> GetSubjectsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SubjectTeacherAssignmentListItemDto>> GetSubjectsForTeacherAssignmentAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TeacherHeaderSubjectDashboardItemDto>> GetHeaderSubjectsForTeacherAsync(string teacherEmail, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SubjectHeaderAssignmentListItemDto>> GetSubjectsForHeaderAssignmentAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TeacherOptionDto>> GetTeacherOptionsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SubjectEditDto?> GetSubjectForEditAsync(Guid subjectId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SubjectDeletePreviewDto?> GetSubjectDeletePreviewAsync(Guid subjectId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(bool Succeeded, string Message, Guid? SubjectId)> CreateSubjectAsync(UpsertSubjectRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> UpdateSubjectAsync(Guid subjectId, UpsertSubjectRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> AssignHeaderTeacherAsync(Guid subjectId, Guid teacherId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> AssignTeacherAsync(Guid subjectId, Guid teacherId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult> DeleteSubjectAsync(Guid subjectId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
