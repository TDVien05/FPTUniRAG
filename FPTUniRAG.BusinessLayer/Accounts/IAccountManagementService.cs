using FPTUniRAG.BusinessLayer.Accounts.Authentication;
using FPTUniRAG.BusinessLayer.Accounts.Importing;
using FPTUniRAG.BusinessLayer.Accounts.Seeding;
using FPTUniRAG.BusinessLayer.Common;
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

    Task EnsureStudentAccountAsync(
        StudentSeedAccount studentAccount,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ManagedAccountDto>> GetManagedAccountsAsync(
        CancellationToken cancellationToken = default);

    Task<AccountSummaryDto> GetAccountSummaryAsync(
        CancellationToken cancellationToken = default);

    Task<ImportStudentsResult> ImportStudentsAsync(
        Stream fileStream,
        string fileName,
        IProgress<StudentImportProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<OperationResult> CreateTeacherAsync(
        string teacherEmail,
        CancellationToken cancellationToken = default);

    Task<OperationResult> SetAccountBlockedStatusAsync(
        Guid userId,
        bool isBlocked,
        CancellationToken cancellationToken = default);

    Task<OperationResult> ChangePasswordAsync(
        string email,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default);

    Task<OperationResult> RequestPasswordResetAsync(
        string email,
        string resetPageUrl,
        CancellationToken cancellationToken = default);

    Task<OperationResult> ResetPasswordAsync(
        string token,
        string newPassword,
        CancellationToken cancellationToken = default);
}
