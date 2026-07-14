using FPTUniRAG.BusinessLayer.Accounts;

namespace FPTUniRAG.BusinessLayer.Services;

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
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
