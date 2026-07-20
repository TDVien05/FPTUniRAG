using FPTUniRAG.BusinessLayer.AdminDashboard;
using FPTUniRAG.DataAccessLayer.Repositories.Reporting;
using Xunit;

namespace FPTUniRAG.Tests.AdminDashboard;

public sealed class StudentChatTurnReconstructorTests
{
    [Fact]
    public void BuildSession_PairsMessagesAndAggregatesUsage()
    {
        var started = new DateTime(2026, 7, 20, 8, 0, 0);
        var prompt = Message("student", "What is dependency injection?", started);
        var answer = Message("assistant", "A design technique.", started.AddSeconds(2), "[{\"citationNumber\":1,\"documentTitle\":\"Week 1\",\"subjectCode\":\"PRN222\",\"subjectName\":\"ASP.NET\",\"chapterTitle\":\"DI\",\"chunkIndex\":2,\"similarityScore\":0.91}]");
        var usage = Usage(answer.MessageId, 120, 40, 160, 1850, "{\"retrievalCount\":4}");

        var result = StudentChatTurnReconstructor.BuildSession(Session([prompt, answer], [usage]));

        var turn = Assert.Single(result.Turns);
        Assert.Equal("answered", turn.Status);
        Assert.Equal(prompt.MessageId, turn.PromptMessageId);
        Assert.Equal(answer.MessageId, turn.AnswerMessageId);
        Assert.Equal(120, turn.PromptTokens);
        Assert.Equal(40, turn.CompletionTokens);
        Assert.Equal(4, turn.RetrievalCount);
        Assert.Single(turn.Citations);
        Assert.Equal(160, result.PromptTokens + result.CompletionTokens);
        Assert.Equal(1850, result.AverageResponseTimeMs);
    }

    [Fact]
    public void BuildSession_LeavesOlderPromptWithoutSavedResponse()
    {
        var started = new DateTime(2026, 7, 20, 8, 0, 0);
        var firstPrompt = Message("student", "First", started);
        var secondPrompt = Message("student", "Second", started.AddSeconds(1));
        var answer = Message("assistant", "Second answer", started.AddSeconds(2));

        var result = StudentChatTurnReconstructor.BuildSession(Session([firstPrompt, secondPrompt, answer], [Usage(answer.MessageId)]));

        Assert.Equal(2, result.Turns.Count);
        Assert.Equal("no-saved-response", result.Turns[0].Status);
        Assert.Equal("answered", result.Turns[1].Status);
        Assert.Equal("Second", result.Turns[1].PromptText);
    }

    [Fact]
    public void BuildSession_PreservesAssistantWithoutPrompt()
    {
        var answer = Message("assistant", "Orphaned stored answer", new DateTime(2026, 7, 20, 8, 0, 0));

        var result = StudentChatTurnReconstructor.BuildSession(Session([answer], []));

        var turn = Assert.Single(result.Turns);
        Assert.Equal("unpaired-response", turn.Status);
        Assert.Null(turn.PromptMessageId);
        Assert.Equal(answer.MessageId, turn.AnswerMessageId);
    }

    [Fact]
    public void BuildSession_IgnoresMalformedCitationAndMetadataJson()
    {
        var started = new DateTime(2026, 7, 20, 8, 0, 0);
        var prompt = Message("student", "Prompt", started);
        var answer = Message("assistant", "Answer", started.AddSeconds(1), "not-json");

        var result = StudentChatTurnReconstructor.BuildSession(Session([prompt, answer], [Usage(answer.MessageId, metadata: "{")]));

        var turn = Assert.Single(result.Turns);
        Assert.Empty(turn.Citations);
        Assert.Null(turn.RetrievalCount);
        Assert.Equal("answered", turn.Status);
    }

    [Fact]
    public void BuildSession_MergesRetriedUsageRowsIntoOneTurn()
    {
        var started = new DateTime(2026, 7, 20, 8, 0, 0);
        var prompt = Message("student", "Prompt", started);
        var answer = Message("assistant", "Answer", started.AddSeconds(5));
        var firstAttempt = Usage(answer.MessageId, 100, 0, 100, 400);
        var secondAttempt = Usage(answer.MessageId, 110, 60, 170, 900);

        var result = StudentChatTurnReconstructor.BuildSession(Session([prompt, answer], [firstAttempt, secondAttempt]));

        var turn = Assert.Single(result.Turns);
        Assert.Equal(210, turn.PromptTokens);
        Assert.Equal(60, turn.CompletionTokens);
        Assert.Equal(270, turn.TotalTokens);
        Assert.Equal(2, turn.RequestCount);
        Assert.Equal(1300, turn.ResponseTimeMs);
        Assert.Equal(result.PromptTokens, turn.PromptTokens);
        Assert.Equal(result.CompletionTokens, turn.CompletionTokens);
        Assert.Equal(1300, result.AverageResponseTimeMs);
    }

    private static StudentChatReportSessionRecord Session(
        IReadOnlyList<StudentChatReportMessageRecord> messages,
        IReadOnlyList<StudentChatReportUsageRecord> usage) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Student One",
            "student@example.com",
            "SE123456",
            Guid.NewGuid(),
            "PRN222",
            "ASP.NET Core",
            messages.FirstOrDefault()?.CreatedAt,
            null,
            messages,
            usage);

    private static StudentChatReportMessageRecord Message(
        string role,
        string content,
        DateTime createdAt,
        string? citationsJson = null) =>
        new(Guid.NewGuid(), role, content, citationsJson, createdAt);

    private static StudentChatReportUsageRecord Usage(
        Guid messageId,
        long promptTokens = 10,
        long completionTokens = 5,
        long totalTokens = 15,
        int responseTimeMs = 500,
        string? metadata = null) =>
        new(
            Guid.NewGuid(),
            messageId,
            "openrouter",
            "example/model",
            promptTokens,
            completionTokens,
            totalTokens,
            1,
            responseTimeMs,
            new DateTime(2026, 7, 20, 8, 0, 2),
            metadata);
}
