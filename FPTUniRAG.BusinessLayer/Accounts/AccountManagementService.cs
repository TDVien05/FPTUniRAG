using FPTUniRAG.BusinessLayer.Accounts.Authentication;
using FPTUniRAG.BusinessLayer.Accounts.Email;
using FPTUniRAG.BusinessLayer.Accounts.Importing;
using FPTUniRAG.BusinessLayer.Accounts.Seeding;
using FPTUniRAG.BusinessLayer.Common;
using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using FPTUniRAG.DataAccessLayer.Repositories.Accounts;
using FPTUniRAG.DataAccessLayer.Entities;
using Microsoft.Extensions.Logging;

namespace FPTUniRAG.BusinessLayer.Accounts;

public sealed class AccountManagementService : IAccountManagementService
{
    private readonly IAccountRepository _accountRepository;
    private readonly IPasswordService _passwordService;
    private readonly ICredentialEmailSender _credentialEmailSender;
    private readonly ILogger<AccountManagementService> _logger;

    public AccountManagementService(
        IAccountRepository accountRepository,
        IPasswordService passwordService,
        ICredentialEmailSender credentialEmailSender,
        ILogger<AccountManagementService> logger)
    {
        _accountRepository = accountRepository;
        _passwordService = passwordService;
        _credentialEmailSender = credentialEmailSender;
        _logger = logger;
    }

    public async Task<AuthenticationResult> AuthenticateAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim();
        if (string.IsNullOrWhiteSpace(normalizedEmail) || string.IsNullOrWhiteSpace(password))
        {
            return new AuthenticationResult(AuthenticationStatus.InvalidCredentials);
        }

        var user = await _accountRepository.FindUserByEmailAsync(normalizedEmail, cancellationToken: cancellationToken);

        if (user is null)
        {
            return new AuthenticationResult(AuthenticationStatus.InvalidCredentials);
        }

        if (!_passwordService.VerifyPassword(user.PasswordHash, password))
        {
            return new AuthenticationResult(AuthenticationStatus.InvalidCredentials);
        }

        if (user.IsBlocked)
        {
            return new AuthenticationResult(AuthenticationStatus.Blocked);
        }

        return new AuthenticationResult(
            AuthenticationStatus.Success,
            new AuthenticatedUser(
                user.UserId,
                user.Email,
                user.FullName,
                NormalizeRole(user.Role),
                user.MustChangePassword));
    }

    public async Task EnsureAdminAccountAsync(
        AdminSeedAccount adminAccount,
        CancellationToken cancellationToken = default)
    {
        await EnsureSeedAccountAsync(adminAccount.Email, adminAccount.Password, adminAccount.DisplayName, "admin", null, cancellationToken);
    }

    public async Task EnsureStudentAccountAsync(
        StudentSeedAccount studentAccount,
        CancellationToken cancellationToken = default)
    {
        await EnsureSeedAccountAsync(studentAccount.Email, studentAccount.Password, studentAccount.DisplayName, "student", studentAccount.StudentCode, cancellationToken);
    }

    private async Task EnsureSeedAccountAsync(
        string email,
        string password,
        string displayName,
        string role,
        string? studentCode,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim();
        var fullName = string.IsNullOrWhiteSpace(displayName) ? "Default User" : displayName.Trim();
        var normalizedStudentCode = string.IsNullOrWhiteSpace(studentCode) ? null : studentCode.Trim();
        var seedUser = new User
        {
            UserId = Guid.NewGuid(), Email = normalizedEmail, FullName = fullName, Role = role,
            StudentCode = normalizedStudentCode, CreatedAt = CreateDatabaseTimestamp(), IsBlocked = false,
            MustChangePassword = false,
            PasswordHash = _passwordService.HashPassword(password)
        };
        await _accountRepository.UpsertSeedUserAsync(seedUser, cancellationToken);
    }

    public async Task<IReadOnlyList<ManagedAccountDto>> GetManagedAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        var users = await _accountRepository.GetAccountsAsync(cancellationToken);

        return users
            .Select(user => new ManagedAccountDto(
                user.UserId,
                NormalizeDisplayName(user.FullName, user.Email),
                user.Email?.Trim() ?? string.Empty,
                NormalizeRole(user.Role),
                user.IsBlocked,
                null,
                null))
            .ToList();
    }

    public async Task<AccountSummaryDto> GetAccountSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        var users = await _accountRepository.GetAccountStatusesAsync(cancellationToken);

        var normalizedUsers = users
            .Select(user => new
            {
                Role = NormalizeRole(user.Role),
                user.IsBlocked
            })
            .ToList();

        return new AccountSummaryDto(
            normalizedUsers.Count,
            normalizedUsers.Count(user => user.Role == "student"),
            normalizedUsers.Count(user => user.Role == "teacher"),
            normalizedUsers.Count(user => user.Role == "admin"),
            normalizedUsers.Count(user => user.IsBlocked));
    }

    public async Task<ImportStudentsResult> ImportStudentsAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var rows = await StudentImportFileParser.ParseAsync(fileStream, fileName, cancellationToken);
        var results = new List<ImportStudentsRowResult>();
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenStudentCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsValidImportRow(row, out var validationError))
            {
                results.Add(new ImportStudentsRowResult(row.RowNumber, row.StudentCode, row.Email, false, validationError));
                continue;
            }

            if (!seenEmails.Add(row.Email))
            {
                results.Add(new ImportStudentsRowResult(row.RowNumber, row.StudentCode, row.Email, false, "Duplicate email in the uploaded file."));
                continue;
            }

            if (!seenStudentCodes.Add(row.StudentCode))
            {
                results.Add(new ImportStudentsRowResult(row.RowNumber, row.StudentCode, row.Email, false, "Duplicate MSSV in the uploaded file."));
                continue;
            }

            if (await _accountRepository.StudentIdentityExistsAsync(row.Email, row.StudentCode, cancellationToken))
            {
                results.Add(new ImportStudentsRowResult(row.RowNumber, row.StudentCode, row.Email, false, "Skipped because the email or MSSV already exists."));
                continue;
            }

            var password = GeneratePassword();
            try
            {
                var user = new User
                {
                    UserId = Guid.NewGuid(),
                    FullName = row.FullName.Trim(),
                    Email = row.Email.Trim(),
                    Role = "student",
                    StudentCode = row.StudentCode.Trim(),
                    CreatedAt = CreateDatabaseTimestamp(),
                    IsBlocked = false,
                    MustChangePassword = true
                };
                user.PasswordHash = _passwordService.HashPassword(password);

                await _accountRepository.CreateUserAsync(user, token =>
                    _credentialEmailSender.SendCredentialsAsync(user.Email, user.FullName, password, "student", token), cancellationToken);
                results.Add(new ImportStudentsRowResult(row.RowNumber, row.StudentCode, row.Email, true, "Student account created and credentials emailed."));
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Failed to import student account for {Email}", row.Email);
                results.Add(new ImportStudentsRowResult(row.RowNumber, row.StudentCode, row.Email, false, $"Failed to create account: {GetDetailedErrorMessage(exception)}"));
            }
        }

        return new ImportStudentsResult(results);
    }

    public async Task<OperationResult> CreateTeacherAsync(
        string teacherEmail,
        CancellationToken cancellationToken = default)
    {
        teacherEmail = teacherEmail.Trim();
        if (string.IsNullOrWhiteSpace(teacherEmail))
        {
            return OperationResult.Failure("Teacher email is required.");
        }

        if (!MailAddress.TryCreate(teacherEmail, out _))
        {
            return OperationResult.Failure("Teacher email is invalid.");
        }

        if (await _accountRepository.TeacherIdentityExistsAsync(teacherEmail, cancellationToken))
        {
            return OperationResult.Failure("A teacher account with this email already exists.");
        }

        var fullName = DeriveNameFromEmail(teacherEmail);
        var password = GeneratePassword();

        try
        {
            var user = new User
            {
                UserId = Guid.NewGuid(),
                FullName = fullName,
                Email = teacherEmail,
                Role = "teacher",
                CreatedAt = CreateDatabaseTimestamp(),
                IsBlocked = false,
                MustChangePassword = true
            };
            user.PasswordHash = _passwordService.HashPassword(password);

            var teacher = new Teacher
            {
                TeacherId = Guid.NewGuid(),
                FullName = fullName,
                Email = teacherEmail,
                CreatedAt = CreateDatabaseTimestamp()
            };

            await _credentialEmailSender.SendCredentialsAsync(
                teacherEmail,
                fullName,
                password,
                "teacher",
                cancellationToken);

            await _accountRepository.CreateTeacherAsync(user, teacher, cancellationToken);
            return OperationResult.Success("Teacher account created and credentials emailed.");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to create teacher account for {Email}", teacherEmail);
            return OperationResult.Failure($"Failed to create teacher account: {GetDetailedErrorMessage(exception)}");
        }
    }

    public async Task<OperationResult> SetAccountBlockedStatusAsync(
        Guid userId,
        bool isBlocked,
        CancellationToken cancellationToken = default)
    {
        var user = await _accountRepository.FindUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return OperationResult.Failure("The selected account no longer exists.");
        }

        if (string.Equals(NormalizeRole(user.Role), "admin", StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Failure("Admin accounts cannot be blocked or reactivated from this screen.");
        }

        user.IsBlocked = isBlocked;
        await _accountRepository.SaveUserAsync(user, cancellationToken);

        return OperationResult.Success(isBlocked
            ? "Account blocked successfully."
            : "Account reactivated successfully.");
    }

    public async Task<OperationResult> ChangePasswordAsync(
        string email,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim();
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return OperationResult.Failure("The current account email is missing.");
        }

        if (string.IsNullOrWhiteSpace(currentPassword))
        {
            return OperationResult.Failure("Current password is required.");
        }

        var passwordValidationError = ValidateNewPassword(newPassword);
        if (passwordValidationError is not null)
        {
            return OperationResult.Failure(passwordValidationError);
        }

        var user = await _accountRepository.FindUserByEmailAsync(normalizedEmail, tracked: true, cancellationToken);

        if (user is null)
        {
            return OperationResult.Failure("The current account no longer exists.");
        }

        if (!_passwordService.VerifyPassword(user.PasswordHash, currentPassword))
        {
            return OperationResult.Failure("Current password is incorrect.");
        }

        if (_passwordService.VerifyPassword(user.PasswordHash, newPassword))
        {
            return OperationResult.Failure("New password must be different from the current password.");
        }

        user.PasswordHash = _passwordService.HashPassword(newPassword);
        user.MustChangePassword = false;
        user.PasswordResetTokenHash = null;
        user.PasswordResetTokenExpiresAt = null;

        await _accountRepository.SaveUserAsync(user, cancellationToken);
        return OperationResult.Success("Password changed successfully.");
    }

    private static string NormalizeRole(string? role)
    {
        return string.IsNullOrWhiteSpace(role)
            ? "student"
            : role.Trim().ToLowerInvariant();
    }

    private static string NormalizeDisplayName(string? fullName, string? email)
    {
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            return fullName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            return email.Trim();
        }

        return "Unknown User";
    }

    private static bool IsValidImportRow(ImportedStudentRow row, out string error)
    {
        if (string.IsNullOrWhiteSpace(row.StudentCode))
        {
            error = "MSSV is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(row.FullName))
        {
            error = "Name is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(row.Email))
        {
            error = "Email is required.";
            return false;
        }

        if (!MailAddress.TryCreate(row.Email, out _))
        {
            error = "Email is invalid.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static DateTime CreateDatabaseTimestamp()
    {
        return DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
    }

    private static string GeneratePassword()
    {
        const string allowedCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%^&*";
        return string.Create(12, allowedCharacters, static (span, characters) =>
        {
            var randomBytes = RandomNumberGenerator.GetBytes(span.Length);
            for (var index = 0; index < span.Length; index++)
            {
                span[index] = characters[randomBytes[index] % characters.Length];
            }
        });
    }

    private static string DeriveNameFromEmail(string email)
    {
        var localPart = email.Split('@', 2)[0];
        var words = localPart
            .Replace('.', ' ')
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length == 0)
        {
            return "Teacher User";
        }

        return string.Join(
            " ",
            words.Select(word => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(word.ToLowerInvariant())));
    }

    private static string? ValidateNewPassword(string? newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
        {
            return "New password is required.";
        }

        if (newPassword.Length < 8)
        {
            return "New password must be at least 8 characters long.";
        }

        return null;
    }

    private static string GetDetailedErrorMessage(Exception exception)
    {
        var builder = new StringBuilder(exception.Message);
        var current = exception.InnerException;

        while (current is not null)
        {
            builder.Append(" ");
            builder.Append(current.Message);
            current = current.InnerException;
        }

        return builder.ToString();
    }
}
