namespace FPTUniRAG.BusinessLayer.AdminDashboard;

public interface IAdminDashboardService
{
    Task<AdminDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default);
}
