namespace FPTUniRAG.BusinessLayer.Accounts.Email;

public sealed class SmtpOptions
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string FromEmail { get; set; } = string.Empty;

    public string FromName { get; set; } = "FPT UniRAG";

    public bool EnableSsl { get; set; } = true;

    public int TimeoutMilliseconds { get; set; } = 10000;

    public string Security { get; set; } = "Auto";
}
