using FPTUniRAG.BusinessLayer.Accounts;
using FPTUniRAG.BusinessLayer.Accounts.Authentication;
using FPTUniRAG.BusinessLayer.Accounts.Email;
using FPTUniRAG.DataAccessLayer.Entities;
using FPTUniRAG.DataAccessLayer.Repositories.Accounts;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using Xunit;

namespace FPTUniRAG.Tests.Accounts;

public sealed class AccountManagementServiceTests
{
    [Fact]
    public async Task CreateTeacherAsync_WhenEmailSendingFails_DoesNotPersistAccount()
    {
        var repository = new RecordingAccountRepository();
        var service = CreateService(repository, new ThrowingCredentialEmailSender());

        var result = await service.CreateTeacherAsync("teacher@fpt.edu.vn");

        Assert.False(result.Succeeded);
        Assert.Contains("SMTP unavailable", result.Message);
        Assert.Equal(0, repository.CreateTeacherCallCount);
    }

    [Fact]
    public async Task CreateTeacherAsync_WhenEmailSendingSucceeds_PersistsUserAndTeacher()
    {
        var repository = new RecordingAccountRepository();
        var service = CreateService(repository, new SuccessfulCredentialEmailSender());

        var result = await service.CreateTeacherAsync("teacher@fpt.edu.vn");

        Assert.True(result.Succeeded);
        Assert.Equal(1, repository.CreateTeacherCallCount);
        Assert.Equal("teacher@fpt.edu.vn", repository.CreatedUser?.Email);
        Assert.True(repository.CreatedUser?.MustChangePassword);
        Assert.Equal("teacher@fpt.edu.vn", repository.CreatedTeacher?.Email);
    }

    [Fact]
    public async Task AuthenticateAsync_ReturnsPasswordChangeRequirement()
    {
        var repository = new RecordingAccountRepository
        {
            ExistingUser = CreateExistingUser(mustChangePassword: true)
        };
        var service = CreateService(repository, new SuccessfulCredentialEmailSender());

        var result = await service.AuthenticateAsync("teacher@fpt.edu.vn", "temporary-password");

        Assert.Equal(AuthenticationStatus.Success, result.Status);
        Assert.True(result.User?.MustChangePassword);
    }

    [Fact]
    public async Task ChangePasswordAsync_ClearsPasswordChangeRequirement()
    {
        var repository = new RecordingAccountRepository
        {
            ExistingUser = CreateExistingUser(mustChangePassword: true)
        };
        var service = CreateService(repository, new SuccessfulCredentialEmailSender());

        var result = await service.ChangePasswordAsync(
            "teacher@fpt.edu.vn",
            "temporary-password",
            "new-password");

        Assert.True(result.Succeeded);
        Assert.False(repository.ExistingUser.MustChangePassword);
        Assert.Equal("hashed:new-password", repository.ExistingUser.PasswordHash);
        Assert.Equal(1, repository.SaveUserCallCount);
    }

    [Fact]
    public async Task EnsureStudentAccountAsync_DoesNotRequirePasswordChangeForSeedAccount()
    {
        var repository = new RecordingAccountRepository();
        var service = CreateService(repository, new SuccessfulCredentialEmailSender());

        await service.EnsureStudentAccountAsync(new(
            "student@fpt.edu.vn",
            "seed-password",
            "Seed Student",
            "SE000001"));

        Assert.NotNull(repository.UpsertedUser);
        Assert.False(repository.UpsertedUser.MustChangePassword);
    }

    [Fact]
    public async Task ImportStudentsAsync_RequiresPasswordChangeForImportedAccount()
    {
        var repository = new RecordingAccountRepository();
        var service = CreateService(repository, new SuccessfulCredentialEmailSender());
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            "MSSV,Name,Email\nSE123456,Imported Student,student@fpt.edu.vn"));

        var result = await service.ImportStudentsAsync(stream, "students.csv");

        Assert.Equal(1, result.CreatedCount);
        Assert.NotNull(repository.CreatedImportedUser);
        Assert.True(repository.CreatedImportedUser.MustChangePassword);
    }

    private static User CreateExistingUser(bool mustChangePassword) => new()
    {
        UserId = Guid.NewGuid(),
        Email = "teacher@fpt.edu.vn",
        FullName = "Teacher",
        Role = "teacher",
        PasswordHash = "hashed:temporary-password",
        MustChangePassword = mustChangePassword
    };

    private static AccountManagementService CreateService(
        IAccountRepository repository,
        ICredentialEmailSender emailSender) =>
        new(
            repository,
            new TestPasswordService(),
            emailSender,
            NullLogger<AccountManagementService>.Instance);

    private sealed class ThrowingCredentialEmailSender : ICredentialEmailSender
    {
        public Task SendCredentialsAsync(
            string email,
            string fullName,
            string password,
            string role,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SMTP unavailable");

        public Task SendPasswordResetLinkAsync(
            string email,
            string fullName,
            string resetLink,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SMTP unavailable");
    }

    private sealed class SuccessfulCredentialEmailSender : ICredentialEmailSender
    {
        public Task SendCredentialsAsync(
            string email,
            string fullName,
            string password,
            string role,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendPasswordResetLinkAsync(
            string email,
            string fullName,
            string resetLink,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestPasswordService : IPasswordService
    {
        public string HashPassword(string password) => $"hashed:{password}";

        public bool VerifyPassword(string passwordHash, string password) =>
            passwordHash == HashPassword(password);
    }

    private sealed class RecordingAccountRepository : IAccountRepository
    {
        public int CreateTeacherCallCount { get; private set; }
        public int SaveUserCallCount { get; private set; }
        public User? CreatedUser { get; private set; }
        public Teacher? CreatedTeacher { get; private set; }
        public User? CreatedImportedUser { get; private set; }
        public User? ExistingUser { get; init; }
        public User? UpsertedUser { get; private set; }

        public Task<bool> TeacherIdentityExistsAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task CreateTeacherAsync(User user, Teacher teacher, CancellationToken cancellationToken = default)
        {
            CreateTeacherCallCount++;
            CreatedUser = user;
            CreatedTeacher = teacher;
            return Task.CompletedTask;
        }

        public Task<User?> FindUserByEmailAsync(string email, bool tracked = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(ExistingUser);

        public Task<User?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<User?>(null);

        public Task<User?> FindUserByPasswordResetTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
            Task.FromResult<User?>(null);

        public Task<IReadOnlyList<AccountListRecord>> GetAccountsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AccountListRecord>>([]);

        public Task<IReadOnlyList<AccountStatusRecord>> GetAccountStatusesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AccountStatusRecord>>([]);

        public Task<bool> StudentIdentityExistsAsync(string email, string studentCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task UpsertSeedUserAsync(User user, CancellationToken cancellationToken = default)
        {
            UpsertedUser = user;
            return Task.CompletedTask;
        }

        public async Task CreateUserAsync(
            User user,
            Func<CancellationToken, Task> beforeCommit,
            CancellationToken cancellationToken = default)
        {
            CreatedImportedUser = user;
            await beforeCommit(cancellationToken);
        }

        public Task SaveUserAsync(User user, CancellationToken cancellationToken = default)
        {
            SaveUserCallCount++;
            return Task.CompletedTask;
        }
    }
}
