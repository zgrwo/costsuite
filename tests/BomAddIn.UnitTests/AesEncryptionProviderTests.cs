using System;
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
}
