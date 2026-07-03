using FPTUniRAG.BusinessLayer.Accounts;
using FPTUniRAG.Options;
using MailKit.Net.Smtp;
using MailKit;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace FPTUniRAG.Services;

public sealed class SmtpCredentialEmailSender : ICredentialEmailSender
{
    private readonly SmtpOptions _options;

    public SmtpCredentialEmailSender(IOptions<SmtpOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendCredentialsAsync(
        string email,
        string fullName,
        string password,
        string role,
        CancellationToken cancellationToken = default)
    {
        var options = NormalizeAndValidateOptions();

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(options.FromName, options.FromEmail));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = $"FPT UniRAG {role} account credentials";
        message.Body = new TextPart("plain")
        {
            Text = $"""
                Hello {fullName},

                Your FPT UniRAG {role} account has been created.

                Email: {email}
                Password: {password}

                Please sign in and change your password after your first login.
                """
        };

        using var client = new SmtpClient();
        client.Timeout = options.TimeoutMilliseconds;

        await client.ConnectAsync(
            options.Host,
            options.Port,
            ResolveSocketOptions(options),
            cancellationToken);

        try
        {
            await client.AuthenticateAsync(options.Username, options.Password, cancellationToken);
            await client.SendAsync(message, cancellationToken);
        }
        catch (AuthenticationException exception)
        {
            throw new InvalidOperationException(
                $"SMTP authentication failed for '{options.Username}'. Check the SMTP host, security mode, and app password. Server message: {exception.Message}",
                exception);
        }
        catch (SmtpCommandException exception)
        {
            throw new InvalidOperationException(
                $"SMTP command failed with status {exception.StatusCode}: {exception.Message}",
                exception);
        }
        catch (SmtpProtocolException exception)
        {
            throw new InvalidOperationException(
                $"SMTP protocol error while sending credentials: {exception.Message}",
                exception);
        }
        finally
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(true, cancellationToken);
            }
        }
    }

    private SmtpOptions NormalizeAndValidateOptions()
    {
        var normalized = new SmtpOptions
        {
            Host = _options.Host.Trim(),
            Port = _options.Port,
            Username = _options.Username.Trim(),
            Password = _options.Password.Replace(" ", string.Empty).Trim(),
            FromEmail = _options.FromEmail.Trim(),
            FromName = string.IsNullOrWhiteSpace(_options.FromName) ? "FPT UniRAG" : _options.FromName.Trim(),
            EnableSsl = _options.EnableSsl,
            TimeoutMilliseconds = _options.TimeoutMilliseconds
        };

        if (string.IsNullOrWhiteSpace(normalized.Host)
            || string.IsNullOrWhiteSpace(normalized.Username)
            || string.IsNullOrWhiteSpace(normalized.Password)
            || string.IsNullOrWhiteSpace(normalized.FromEmail))
        {
            throw new InvalidOperationException("SMTP settings are incomplete. Configure the Smtp section in appsettings.json.");
        }

        if (string.Equals(normalized.Host, "smtp.example.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("SMTP host is still set to the placeholder smtp.example.com. Replace it with your real SMTP host, such as smtp.gmail.com.");
        }

        return normalized;
    }

    private static SecureSocketOptions ResolveSocketOptions(SmtpOptions options)
    {
        return options.Security.Trim().ToLowerInvariant() switch
        {
            "none" => SecureSocketOptions.None,
            "ssl" => SecureSocketOptions.SslOnConnect,
            "sslonconnect" => SecureSocketOptions.SslOnConnect,
            "starttls" => SecureSocketOptions.StartTls,
            "starttlswhenavailable" => SecureSocketOptions.StartTlsWhenAvailable,
            _ => options.Port == 465
                ? SecureSocketOptions.SslOnConnect
                : options.EnableSsl
                    ? SecureSocketOptions.StartTls
                    : SecureSocketOptions.None
        };
    }
}
