namespace BomAddIn.Infrastructure.Security
{
    public interface IEncryptionProvider
    {
        byte[] Protect(byte[] data);
        byte[] Unprotect(byte[] data);
    }
}
