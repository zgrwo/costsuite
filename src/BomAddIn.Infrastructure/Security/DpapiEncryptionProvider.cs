using System;
using System.Security.Cryptography;
using BomAddIn.Infrastructure.Logging;

namespace BomAddIn.Infrastructure.Security
{
    /// <summary>Windows DPAPI 加密提供者 — 用于保护 AES DEK</summary>
    public class DpapiEncryptionProvider : IEncryptionProvider
    {
        // 应用特定熵 — 增加隔离层，防止同机其他应用解密本插件数据
        private static readonly byte[] Entropy = new byte[]
        {
            0x42, 0x6F, 0x6D, 0x41, 0x64, 0x64, 0x49, 0x6E,  // "BomAddIn"
            0x76, 0x31, 0x2E, 0x31                                // "v1.1"
        };

        public byte[] Protect(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            try
            {
                return ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
            }
            catch (CryptographicException ex)
            {
                AppLogger.Error($"DPAPI 加密失败: {ex.Message}", ex, typeof(DpapiEncryptionProvider));
                throw;
            }
        }

        public byte[] Unprotect(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            try
            {
                return ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
            }
            catch (CryptographicException ex)
            {
                AppLogger.Error($"DPAPI 解密失败: {ex.Message}", ex, typeof(DpapiEncryptionProvider));
                throw;
            }
        }
    }
}
