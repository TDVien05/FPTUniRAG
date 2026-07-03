namespace FPTUniRAG.BusinessLayer.Accounts;

public interface IPasswordService
{
    string HashPassword(string password);

    bool VerifyPassword(string passwordHash, string password);
}
