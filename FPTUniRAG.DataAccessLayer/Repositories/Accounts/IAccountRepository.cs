using FPTUniRAG.DataAccessLayer.Entities;

namespace FPTUniRAG.DataAccessLayer.Repositories.Accounts;

public interface IAccountRepository
{
    Task<User?> FindUserByEmailAsync(string email, bool tracked = false, CancellationToken cancellationToken = default);
    Task<User?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccountListRecord>> GetAccountsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccountStatusRecord>> GetAccountStatusesAsync(CancellationToken cancellationToken = default);
    Task<bool> StudentIdentityExistsAsync(string email, string studentCode, CancellationToken cancellationToken = default);
    Task<bool> TeacherIdentityExistsAsync(string email, CancellationToken cancellationToken = default);
    Task UpsertSeedUserAsync(User user, CancellationToken cancellationToken = default);
    Task CreateUserAsync(User user, Func<CancellationToken, Task> beforeCommit, CancellationToken cancellationToken = default);
    Task CreateTeacherAsync(User user, Teacher teacher, Func<CancellationToken, Task> beforeCommit, CancellationToken cancellationToken = default);
    Task SaveUserAsync(User user, CancellationToken cancellationToken = default);
}

public sealed record AccountListRecord(Guid UserId, string? FullName, string Email, string? Role, bool IsBlocked);
public sealed record AccountStatusRecord(string? Role, bool IsBlocked);
