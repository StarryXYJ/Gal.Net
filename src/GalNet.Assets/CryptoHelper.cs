using System.Security.Cryptography;

namespace GalNet.Assets;

/// <summary>
/// 加密/解密/Hash 工具类。
/// 使用 .NET 内置的 System.Security.Cryptography。
/// </summary>
public static class CryptoHelper
{
    private const int KeySize = 256;
    private const int BlockSize = 128;
    private const int SaltLength = 32;

    /// <summary>计算数据的 SHA256 哈希值（十六进制小写字符串）。</summary>
    public static string HashSHA256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>计算数据的 SHA256 哈希值。</summary>
    public static string HashSHA256(ReadOnlySpan<byte> data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>计算流的 SHA256 哈希值。</summary>
    public static string HashSHA256(Stream stream)
    {
        var hash = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// 使用 AES-256-CBC 加密数据。
    /// </summary>
    /// <param name="data">明文数据</param>
    /// <param name="password">密码（将经过 PBKDF2 派生密钥）</param>
    /// <returns>加密后的数据（格式：盐值(32B) + IV(16B) + 密文）</returns>
    public static byte[] EncryptAES(byte[] data, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var key = DeriveKey(password, salt);
        var iv = RandomNumberGenerator.GetBytes(BlockSize / 8);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var ciphertext = encryptor.TransformFinalBlock(data, 0, data.Length);

        // 输出: salt + iv + ciphertext
        var result = new byte[salt.Length + iv.Length + ciphertext.Length];
        Buffer.BlockCopy(salt, 0, result, 0, salt.Length);
        Buffer.BlockCopy(iv, 0, result, salt.Length, iv.Length);
        Buffer.BlockCopy(ciphertext, 0, result, salt.Length + iv.Length, ciphertext.Length);
        return result;
    }

    /// <summary>
    /// 使用 AES-256-CBC 解密数据。
    /// </summary>
    /// <param name="encryptedData">加密数据（格式：盐值(32B) + IV(16B) + 密文）</param>
    /// <param name="password">密码</param>
    /// <returns>明文数据</returns>
    public static byte[] DecryptAES(byte[] encryptedData, string password)
    {
        var salt = encryptedData[..SaltLength];
        var iv = encryptedData[SaltLength..(SaltLength + BlockSize / 8)];
        var ciphertext = encryptedData[(SaltLength + BlockSize / 8)..];

        var key = DeriveKey(password, salt);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
    }

    /// <summary>验证数据的哈希值是否匹配。</summary>
    public static bool VerifyHash(byte[] data, string expectedHash)
    {
        var actual = HashSHA256(data);
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(actual),
            System.Text.Encoding.UTF8.GetBytes(expectedHash));
    }

    /// <summary>使用 PBKDF2 从密码派生 AES 密钥。</summary>
    private static byte[] DeriveKey(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations: 100_000,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: KeySize / 8);
    }
}
