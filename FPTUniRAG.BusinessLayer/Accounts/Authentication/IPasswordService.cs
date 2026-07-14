namespace FPTUniRAG.BusinessLayer.Accounts.Authentication;

public interface IPasswordService
{
    string HashPassword(string password);

    bool VerifyPassword(string passwordHash, string password);
}
