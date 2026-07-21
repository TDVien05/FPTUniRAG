namespace FPTUniRAG.BusinessLayer.Accounts.Email;

public interface ICredentialEmailSender
{
    Task SendCredentialsAsync(
        string email,
        string fullName,
        string password,
        string role,
        CancellationToken cancellationToken = default);

    Task SendPasswordResetLinkAsync(
        string email,
        string fullName,
        string resetLink,
        CancellationToken cancellationToken = default);
}
