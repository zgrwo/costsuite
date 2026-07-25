using BomAddIn.Infrastructure.Models;

namespace BomAddIn.Core.Services
{
    public interface IAuthService
    {
        AuthResult Authenticate(string username, string password);
        void Logout(long userId);
        User? GetCurrentUser(long userId);
        void SeedAdminUser(string? password = null);
    }

    public class AuthResult
    {
        public bool Success { get; set; }
        public string? Token { get; set; }
        public string? ErrorMessage { get; set; }
        public User? User { get; set; }
    }
}
