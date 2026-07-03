using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FPTUniRAG.BusinessLayer.Accounts;

public sealed class AccountManagementService : IAccountManagementService
{
    private readonly AppDbContext _dbContext;
    private readonly IPasswordService _passwordService;
    private readonly ICredentialEmailSender _credentialEmailSender;
    private readonly ILogger<AccountManagementService> _logger;

    public AccountManagementService(
        AppDbContext dbContext,
        IPasswordService passwordService,
        ICredentialEmailSender credentialEmailSender,
        ILogger<AccountManagementService> logger)
    {
        _dbContext = dbContext;
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

        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Email.ToLower() == normalizedEmail.ToLower(),
                cancellationToken);

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
                NormalizeRole(user.Role)));
    }

    public async Task EnsureAdminAccountAsync(
        AdminSeedAccount adminAccount,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = adminAccount.Email.Trim();
        var fullName = string.IsNullOrWhiteSpace(adminAccount.DisplayName)
            ? "Admin User"
            : adminAccount.DisplayName.Trim();

        var existingUser = await _dbContext.Users
            .FirstOrDefaultAsync(
                user => user.Email.ToLower() == normalizedEmail.ToLower(),
                cancellationToken);

        if (existingUser is null)
        {
            existingUser = new User
            {
                UserId = Guid.NewGuid(),
                Email = normalizedEmail,
                FullName = fullName,
                Role = "admin",
                CreatedAt = CreateDatabaseTimestamp(),
                IsBlocked = false
            };
            existingUser.PasswordHash = _passwordService.HashPassword(adminAccount.Password);
            _dbContext.Users.Add(existingUser);
        }
        else
        {
            existingUser.Email = normalizedEmail;
            existingUser.FullName = fullName;
            existingUser.Role = "admin";
            existingUser.IsBlocked = false;
            existingUser.PasswordHash = _passwordService.HashPassword(adminAccount.Password);
            existingUser.PasswordResetTokenHash = null;
            existingUser.PasswordResetTokenExpiresAt = null;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ManagedAccountDto>> GetManagedAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .OrderBy(user => user.Role)
            .ThenBy(user => user.FullName)
            .Select(user => new ManagedAccountDto(
                user.UserId,
                user.FullName,
                user.Email,
                NormalizeRole(user.Role),
                user.IsBlocked,
                user.StudentCode,
                user.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<AccountSummaryDto> GetAccountSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        var users = await _dbContext.Users
            .AsNoTracking()
            .Select(user => new
            {
                Role = NormalizeRole(user.Role),
                user.IsBlocked
            })
            .ToListAsync(cancellationToken);

        return new AccountSummaryDto(
            users.Count,
            users.Count(user => user.Role == "student"),
            users.Count(user => user.Role == "teacher"),
            users.Count(user => user.Role == "admin"),
            users.Count(user => user.IsBlocked));
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

            if (await _dbContext.Users.AnyAsync(
                    user => user.Email.ToLower() == row.Email.ToLower()
                        || (user.StudentCode != null && user.StudentCode.ToLower() == row.StudentCode.ToLower()),
                    cancellationToken))
            {
                results.Add(new ImportStudentsRowResult(row.RowNumber, row.StudentCode, row.Email, false, "Skipped because the email or MSSV already exists."));
                continue;
            }

            var password = GeneratePassword();
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

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
                    IsBlocked = false
                };
                user.PasswordHash = _passwordService.HashPassword(password);

                _dbContext.Users.Add(user);
                await _dbContext.SaveChangesAsync(cancellationToken);

                await _credentialEmailSender.SendCredentialsAsync(
                    user.Email,
                    user.FullName,
                    password,
                    "student",
                    cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                results.Add(new ImportStudentsRowResult(row.RowNumber, row.StudentCode, row.Email, true, "Student account created and credentials emailed."));
            }
            catch (Exception exception)
            {
                await transaction.RollbackAsync(cancellationToken);
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

        if (await _dbContext.Users.AnyAsync(user => user.Email.ToLower() == teacherEmail.ToLower(), cancellationToken)
            || await _dbContext.Teachers.AnyAsync(teacher => teacher.Email != null && teacher.Email.ToLower() == teacherEmail.ToLower(), cancellationToken))
        {
            return OperationResult.Failure("A teacher account with this email already exists.");
        }

        var fullName = DeriveNameFromEmail(teacherEmail);
        var password = GeneratePassword();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var user = new User
            {
                UserId = Guid.NewGuid(),
                FullName = fullName,
                Email = teacherEmail,
                Role = "teacher",
                CreatedAt = CreateDatabaseTimestamp(),
                IsBlocked = false
            };
            user.PasswordHash = _passwordService.HashPassword(password);

            var teacher = new Teacher
            {
                TeacherId = Guid.NewGuid(),
                FullName = fullName,
                Email = teacherEmail,
                CreatedAt = CreateDatabaseTimestamp()
            };

            _dbContext.Users.Add(user);
            _dbContext.Teachers.Add(teacher);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _credentialEmailSender.SendCredentialsAsync(
                teacherEmail,
                fullName,
                password,
                "teacher",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Success("Teacher account created and credentials emailed.");
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogWarning(exception, "Failed to create teacher account for {Email}", teacherEmail);
            return OperationResult.Failure($"Failed to create teacher account: {GetDetailedErrorMessage(exception)}");
        }
    }

    public async Task<OperationResult> SetAccountBlockedStatusAsync(
        Guid userId,
        bool isBlocked,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);
        if (user is null)
        {
            return OperationResult.Failure("The selected account no longer exists.");
        }

        if (string.Equals(NormalizeRole(user.Role), "admin", StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Failure("Admin accounts cannot be blocked or reactivated from this screen.");
        }

        user.IsBlocked = isBlocked;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return OperationResult.Success(isBlocked
            ? "Account blocked successfully."
            : "Account reactivated successfully.");
    }

    private static string NormalizeRole(string? role)
    {
        return string.IsNullOrWhiteSpace(role)
            ? "student"
            : role.Trim().ToLowerInvariant();
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
