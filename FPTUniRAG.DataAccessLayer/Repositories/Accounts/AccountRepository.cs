using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;

namespace FPTUniRAG.DataAccessLayer.Repositories.Accounts;

public sealed class AccountRepository(AppDbContext context) : IAccountRepository
{
    public Task<User?> FindUserByEmailAsync(string email, bool tracked = false, CancellationToken cancellationToken = default)
    {
        var query = tracked ? context.Users : context.Users.AsNoTracking();
        return query.FirstOrDefaultAsync(user => user.Email.ToLower() == email.ToLower(), cancellationToken);
    }

    public Task<User?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.Users.FirstOrDefaultAsync(user => user.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<AccountListRecord>> GetAccountsAsync(CancellationToken cancellationToken = default) =>
        await context.Users.AsNoTracking().OrderBy(user => user.Role).ThenBy(user => user.FullName)
            .Select(user => new AccountListRecord(user.UserId, user.FullName, user.Email, user.Role, user.IsBlocked))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AccountStatusRecord>> GetAccountStatusesAsync(CancellationToken cancellationToken = default) =>
        await context.Users.AsNoTracking().Select(user => new AccountStatusRecord(user.Role, user.IsBlocked)).ToListAsync(cancellationToken);

    public Task<bool> StudentIdentityExistsAsync(string email, string studentCode, CancellationToken cancellationToken = default) =>
        context.Users.AnyAsync(user => user.Email.ToLower() == email.ToLower() ||
            (user.StudentCode != null && user.StudentCode.ToLower() == studentCode.ToLower()), cancellationToken);

    public async Task<bool> TeacherIdentityExistsAsync(string email, CancellationToken cancellationToken = default) =>
        await context.Users.AnyAsync(user => user.Email.ToLower() == email.ToLower(), cancellationToken) ||
        await context.Teachers.AnyAsync(teacher => teacher.Email != null && teacher.Email.ToLower() == email.ToLower(), cancellationToken);

    public async Task UpsertSeedUserAsync(User user, CancellationToken cancellationToken = default)
    {
        var existing = await context.Users.FirstOrDefaultAsync(candidate => candidate.Email.ToLower() == user.Email.ToLower(), cancellationToken);
        if (existing is null) context.Users.Add(user);
        else
        {
            existing.Email = user.Email; existing.FullName = user.FullName; existing.Role = user.Role;
            existing.StudentCode = user.StudentCode; existing.IsBlocked = user.IsBlocked; existing.PasswordHash = user.PasswordHash;
            existing.PasswordResetTokenHash = null; existing.PasswordResetTokenExpiresAt = null;
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task CreateUserAsync(User user, Func<CancellationToken, Task> beforeCommit, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        context.Users.Add(user);
        await context.SaveChangesAsync(cancellationToken);
        await beforeCommit(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CreateTeacherAsync(User user, Teacher teacher, Func<CancellationToken, Task> beforeCommit, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        context.Users.Add(user); context.Teachers.Add(teacher);
        await context.SaveChangesAsync(cancellationToken);
        await beforeCommit(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveUserAsync(User user, CancellationToken cancellationToken = default)
    {
        if (context.Entry(user).State == EntityState.Detached) context.Users.Update(user);
        await context.SaveChangesAsync(cancellationToken);
    }
}
