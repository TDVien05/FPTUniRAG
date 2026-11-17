using FPTUniRAG.BusinessLayer.Accounts;
using FPTUniRAG.BusinessLayer.Accounts.Authentication;
using FPTUniRAG.DataAccessLayer.Entities;
using FPTUniRAG.DataAccessLayer.Repositories.Accounts;
using FPTUniRAG.DataAccessLayer.Repositories.Seeding;

namespace FPTUniRAG.BusinessLayer.Accounts.Seeding;

public sealed class AdminAccountInitializationHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;

    public AdminAccountInitializationHostedService(
        IServiceProvider serviceProvider,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var email = _configuration["AdminCredentials:Email"];
        var password = _configuration["AdminCredentials:Password"];
        var displayName = _configuration["AdminCredentials:DisplayName"];
        var studentEmail = _configuration["StudentCredentials:Email"];
        var studentPassword = _configuration["StudentCredentials:Password"];
        var studentDisplayName = _configuration["StudentCredentials:DisplayName"];
        var studentCode = _configuration["StudentCredentials:StudentCode"];

        var adminAccount = new AdminSeedAccount(
            string.IsNullOrWhiteSpace(email) ? "admin@fpt.edu.vn" : email.Trim(),
            string.IsNullOrWhiteSpace(password) ? "Admin@123" : password,
            string.IsNullOrWhiteSpace(displayName) ? "Admin User" : displayName.Trim());

        var studentAccount = new StudentSeedAccount(
            string.IsNullOrWhiteSpace(studentEmail) ? "student@fpt.edu.vn" : studentEmail.Trim(),
            string.IsNullOrWhiteSpace(studentPassword) ? "Student@123" : studentPassword,
            string.IsNullOrWhiteSpace(studentDisplayName) ? "Default Student" : studentDisplayName.Trim(),
            string.IsNullOrWhiteSpace(studentCode) ? "STU000001" : studentCode.Trim());

        using var scope = _serviceProvider.CreateScope();
        var accountManagementService = scope.ServiceProvider.GetRequiredService<IAccountManagementService>();
        await accountManagementService.EnsureAdminAccountAsync(adminAccount, cancellationToken);
        await accountManagementService.EnsureStudentAccountAsync(studentAccount, cancellationToken);

        await SeedTeacherAndDemoCatalogAsync(scope.ServiceProvider, cancellationToken);
    }

    private async Task SeedTeacherAndDemoCatalogAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        // Set DemoData:Enabled=false to skip seeding the demo teacher and subjects.
        if (string.Equals(_configuration["DemoData:Enabled"], "false", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var email = OrDefault(_configuration["TeacherCredentials:Email"], "teacher@fpt.edu.vn");
        var password = OrDefault(_configuration["TeacherCredentials:Password"], "Teacher@123", trim: false);
        var displayName = OrDefault(_configuration["TeacherCredentials:DisplayName"], "Default Teacher");

        var accountRepository = services.GetRequiredService<IAccountRepository>();
        var passwordService = services.GetRequiredService<IPasswordService>();
        var demoDataRepository = services.GetRequiredService<IDemoDataRepository>();

        await accountRepository.UpsertSeedUserAsync(new User
        {
            UserId = Guid.NewGuid(),
            Email = email,
            FullName = displayName,
            Role = "teacher",
            CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
            IsBlocked = false,
            MustChangePassword = false,
            PasswordHash = passwordService.HashPassword(password)
        }, cancellationToken);

        await demoDataRepository.EnsureTeacherWithSubjectsAsync(
            email, displayName, "Software Engineering", DemoSubjects, cancellationToken);
    }

    private static string OrDefault(string? value, string fallback, bool trim = true) =>
        string.IsNullOrWhiteSpace(value) ? fallback : (trim ? value.Trim() : value);

    private static readonly IReadOnlyList<DemoSubjectSeed> DemoSubjects =
    [
        new("PRN222", "Advanced Cross-Platform Application Programming With .NET",
            "ASP.NET Core Razor Pages, SignalR, EF Core and layered architecture.", "fixed",
            [new("Chapter 1: ASP.NET Core Razor Pages", 1), new("Chapter 2: Entity Framework Core", 2), new("Chapter 3: SignalR Realtime", 3)]),
        new("SWE201c", "Introduction to Software Engineering",
            "Software process models, requirements, design, testing and maintenance.", "semantic",
            [new("Chapter 1: Software Process Models", 1), new("Chapter 2: Requirements Engineering", 2), new("Chapter 3: Software Testing", 3)]),
        new("DBI202", "Database Systems",
            "Relational model, SQL, normalization and transactions.", "fixed",
            [new("Chapter 1: Relational Model", 1), new("Chapter 2: SQL", 2), new("Chapter 3: Normalization", 3)])
    ];

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
