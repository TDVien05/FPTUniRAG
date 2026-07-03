namespace FPTUniRAG.BusinessLayer.Accounts;

public interface IAccountManagementService
{
    Task<AuthenticationResult> AuthenticateAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default);

    Task EnsureAdminAccountAsync(
        AdminSeedAccount adminAccount,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ManagedAccountDto>> GetManagedAccountsAsync(
        CancellationToken cancellationToken = default);

    Task<AccountSummaryDto> GetAccountSummaryAsync(
        CancellationToken cancellationToken = default);

    Task<ImportStudentsResult> ImportStudentsAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken = default);

    Task<OperationResult> CreateTeacherAsync(
        string teacherEmail,
        CancellationToken cancellationToken = default);

    Task<OperationResult> SetAccountBlockedStatusAsync(
        Guid userId,
        bool isBlocked,
        CancellationToken cancellationToken = default);
}
