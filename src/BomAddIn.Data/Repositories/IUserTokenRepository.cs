using System;
using System.Collections.Generic;
using BomAddIn.Infrastructure.Models;

namespace BomAddIn.Data.Repositories
{
    public interface IUserTokenRepository
    {
        void Add(UserToken token);
        UserToken? GetByHash(string tokenHash);
        void Revoke(string tokenHash);
        void RevokeAllForUser(long userId);
        int CleanupExpired(DateTime before);
    }
}
