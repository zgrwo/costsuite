using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BomAddIn.Infrastructure.Security;
using FluentAssertions;
using Xunit;

namespace BomAddIn.UnitTests;

/// <summary>
/// AesEncryptionProvider 测试 — DEK 持久化、加密往返、损坏恢复。
/// 覆盖审查发现 H-8：DEK 文件损坏时的行为。
/// </summary>
public class AesEncryptionProviderTests
{
    // DPAPI 在测试环境中可正常工作（Windows 用户级别密钥保护）
    private readonly DpapiEncryptionProvider _dpapi = new();

    [Fact]
    public void Constructor_WithNullKeyProtector_ThrowsArgumentNullException()
    {
        Action act = () => new AesEncryptionProvider(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ProtectUnprotect_RoundTrip_ProducesOriginalData()
    {
        var provider = new AesEncryptionProvider(_dpapi);
        var original = Encoding.UTF8.GetBytes("price_data_12345_CNY");

        var ciphertext = provider.Protect(original);
        ciphertext.Should().NotBeNull();
        ciphertext.Should().NotBeEmpty();

        var decrypted = provider.Unprotect(ciphertext);
        decrypted.Should().Equal(original);
    }

    [Fact]
    public void ProtectUnprotect_MultipleCycles_SameKeyReused()
    {
        var provider = new AesEncryptionProvider(_dpapi);
        var original = Encoding.UTF8.GetBytes("repeated_encryption_test");

        for (int i = 0; i < 5; i++)
        {
            var cipher = provider.Protect(original);
            var plain = provider.Unprotect(cipher);
            plain.Should().Equal(original);
        }

        provider.Dispose();
    }

    [Fact]
    public void Unprotect_EmptyArray_ReturnsEmpty()
    {
        var provider = new AesEncryptionProvider(_dpapi);

        // 空加密数据无法解密，因为缺少 IV+DEK 头部
        Action act = () => provider.Unprotect(Array.Empty<byte>());
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void Unprotect_Null_ThrowsArgumentNullException()
    {
        var provider = new AesEncryptionProvider(_dpapi);

        Action act = () => provider.Unprotect(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Protect_Null_ThrowsArgumentNullException()
    {
        var provider = new AesEncryptionProvider(_dpapi);

        Action act = () => provider.Protect(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── H-8 补充: DEK 文件损坏 + 密文篡改回归测试 ──

    [Fact]
    public void Unprotect_TamperedData_ThrowsCryptographicException()
    {
        // Arrange: 生成合法密文
        var provider = new AesEncryptionProvider(_dpapi);
        var original = Encoding.UTF8.GetBytes("sensitive_price_data_2026");
        var ciphertext = provider.Protect(original);

        // Act: 篡改密文末字节（PKCS7 填充验证依赖末字节 → 篡改必触发异常）
        // 注意: 篡改 IV 首字节只破坏第一块明文，不影响 PKCS7 填充尾块
        ciphertext[ciphertext.Length - 1] ^= 0xFF;

        Action act = () => provider.Unprotect(ciphertext);

        // Assert: 篡改后解密应抛出 CryptographicException
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Constructor_DekFileTruncated_ThrowsCryptographicException()
    {
        // AesEncryptionProvider 构造函数从固定路径加载 DEK 文件:
        //   %LocalAppData%/BomAddIn/Data/aes-dek.dat
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var keyDir = Path.Combine(appData, "BomAddIn", "Data");
        var keyFile = Path.Combine(keyDir, "aes-dek.dat");

        // 保存已有合法 DEK 文件（如果存在），测试结束后恢复
        byte[]? originalContent = null;
        if (File.Exists(keyFile))
        {
            originalContent = File.ReadAllBytes(keyFile);
            File.Delete(keyFile);
        }

        try
        {
            // 写入截断 DEK 文件（仅 8 字节 — 非有效 DPAPI blob）
            Directory.CreateDirectory(keyDir);
            File.WriteAllBytes(keyFile, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x01, 0x02, 0x03 });

            // Act: 构造 AesEncryptionProvider — LoadOrCreateDek 读取受损 DEK 应抛出
            Action act = () => new AesEncryptionProvider(_dpapi);

            // Assert: DPAPI 解密非法 blob 应抛出 CryptographicException
            act.Should().Throw<CryptographicException>();
        }
        finally
        {
            // 清理测试文件并恢复原始 DEK
            if (File.Exists(keyFile))
                File.Delete(keyFile);
            if (originalContent != null)
                File.WriteAllBytes(keyFile, originalContent);
        }
    }
}
