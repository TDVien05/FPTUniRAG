using FPTUniRAG.BusinessLayer.Accounts;

namespace FPTUniRAG.Services;

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

        var adminAccount = new AdminSeedAccount(
            string.IsNullOrWhiteSpace(email) ? "admin@fpt.edu.vn" : email.Trim(),
            string.IsNullOrWhiteSpace(password) ? "Admin@123" : password,
            string.IsNullOrWhiteSpace(displayName) ? "Admin User" : displayName.Trim());

        using var scope = _serviceProvider.CreateScope();
        var accountManagementService = scope.ServiceProvider.GetRequiredService<IAccountManagementService>();
        await accountManagementService.EnsureAdminAccountAsync(adminAccount, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
