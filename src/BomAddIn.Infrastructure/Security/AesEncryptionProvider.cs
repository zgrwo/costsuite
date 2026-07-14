using System;
using System.IO;
using System.Security.Cryptography;
using BomAddIn.Infrastructure.Logging;

namespace BomAddIn.Infrastructure.Security
{
    /// <summary>
    /// AES-256-CBC 加密提供者 — 用于价格数据静态加密。
    /// DEK (Data Encryption Key) 由 Windows DPAPI 保护。
    /// 符合 spec §11.3 的安全架构要求。
    ///
    /// 密文格式: [16-byte IV][DPAPI-protected DEK length (4 bytes)][DPAPI-protected DEK][AES ciphertext]
    /// </summary>
    public class AesEncryptionProvider : IEncryptionProvider, IDisposable
    {
        private readonly DpapiEncryptionProvider _keyProtector; // DPAPI for DEK protection
        private readonly byte[] _dek;                      // AES-256 key (32 bytes)
        private readonly string _keyFilePath;
        private bool _disposed;

        /// <summary>
        /// 创建 AES 加密提供者。
        /// DEK 存储在 %LocalAppData%/BomAddIn/Data/aes-dek.dat，由 DPAPI 保护。
        /// </summary>
        public AesEncryptionProvider(DpapiEncryptionProvider keyProtector)
        {
            _keyProtector = keyProtector ?? throw new ArgumentNullException(nameof(keyProtector));

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var keyDir = Path.Combine(appData, "BomAddIn", "Data");
            Directory.CreateDirectory(keyDir);
            _keyFilePath = Path.Combine(keyDir, "aes-dek.dat");

            _dek = LoadOrCreateDek();
        }

        public byte[] Protect(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (_disposed) throw new ObjectDisposedException(nameof(AesEncryptionProvider));

            try
            {
                using var aes = Aes.Create();
                aes.Key = _dek;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.GenerateIV();

                var protectedDek = _keyProtector.Protect(_dek);

                using var ms = new MemoryStream();
                // 写入 IV (16 bytes)
                ms.Write(aes.IV, 0, aes.IV.Length);
                // 写入受保护的 DEK 长度 (4 bytes, big-endian)
                var lenBytes = BitConverter.GetBytes(protectedDek.Length);
                if (BitConverter.IsLittleEndian) Array.Reverse(lenBytes);
                ms.Write(lenBytes, 0, 4);
                // 写入受保护的 DEK
                ms.Write(protectedDek, 0, protectedDek.Length);

                // 加密数据
                using var encryptor = aes.CreateEncryptor();
                using var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
                cs.Write(data, 0, data.Length);
                cs.FlushFinalBlock();

                return ms.ToArray();
            }
            catch (CryptographicException ex)
            {
                AppLogger.Error($"AES-256 加密失败: {ex.Message}", ex, typeof(AesEncryptionProvider));
                throw;
            }
        }

        public byte[] Unprotect(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (_disposed) throw new ObjectDisposedException(nameof(AesEncryptionProvider));

            try
            {
                using var ms = new MemoryStream(data);

                // 读取 IV (16 bytes)
                var iv = new byte[16];
                if (ms.Read(iv, 0, 16) != 16)
                    throw new CryptographicException("密文格式无效: IV 长度不足。");

                // 读取受保护的 DEK 长度 (4 bytes, big-endian)
                var lenBytes = new byte[4];
                ms.Read(lenBytes, 0, 4);
                if (BitConverter.IsLittleEndian) Array.Reverse(lenBytes);
                var dekLength = BitConverter.ToInt32(lenBytes, 0);
                if (dekLength <= 0 || dekLength > ms.Length - ms.Position)
                    throw new CryptographicException("密文格式无效: DEK 长度异常。");

                // 读取受保护的 DEK
                var protectedDek = new byte[dekLength];
                ms.Read(protectedDek, 0, dekLength);

                // DPAPI 解密 DEK
                var dek = _keyProtector.Unprotect(protectedDek);

                // AES-CBC 解密剩余数据
                var ciphertext = new byte[ms.Length - ms.Position];
                ms.Read(ciphertext, 0, ciphertext.Length);

                using var aes = Aes.Create();
                aes.Key = dek;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.IV = iv;

                using var decryptor = aes.CreateDecryptor();
                using var outMs = new MemoryStream(ciphertext);
                using var cs = new CryptoStream(outMs, decryptor, CryptoStreamMode.Read);
                using var resultMs = new MemoryStream();
                cs.CopyTo(resultMs);
                return resultMs.ToArray();
            }
            catch (CryptographicException ex)
            {
                AppLogger.Error($"AES-256 解密失败: {ex.Message}", ex, typeof(AesEncryptionProvider));
                throw;
            }
        }

        private byte[] LoadOrCreateDek()
        {
            try
            {
                if (File.Exists(_keyFilePath))
                {
                    var protectedDek = File.ReadAllBytes(_keyFilePath);
                    return _keyProtector.Unprotect(protectedDek);
                }
            }
            catch (Exception ex)
            {
                // DPAPI 解密失败时直接抛出异常，防止静默生成新密钥破坏已有加密数据
                AppLogger.Error($"DEK 解密失败。Windows 用户凭证变更导致加密数据无法读取。请使用备份恢复。",
                    ex, typeof(AesEncryptionProvider));
                throw new System.Security.Cryptography.CryptographicException(
                    "DEK 解密失败。Windows 用户凭证变更导致加密数据无法读取。请使用备份恢复。", ex);
            }

            // 仅当 DEK 文件不存在时生成新密钥（首次初始化）
            // 生成新 AES-256 密钥
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.GenerateKey();
            var newDek = aes.Key;

            // DPAPI 保护后持久化
            try
            {
                var protectedDek = _keyProtector.Protect(newDek);
                File.WriteAllBytes(_keyFilePath, protectedDek);
                AppLogger.Info("已生成并持久化新的 AES-256 DEK。", typeof(AesEncryptionProvider));
            }
            catch (Exception ex)
            {
                AppLogger.Error($"DEK 持久化失败（密钥仅存在于内存中，重启后数据将不可读）: {ex.Message}",
                    ex, typeof(AesEncryptionProvider));
            }

            return newDek;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // 清除内存中的 DEK（尽力而为）
            Array.Clear(_dek, 0, _dek.Length);
        }
    }
}
