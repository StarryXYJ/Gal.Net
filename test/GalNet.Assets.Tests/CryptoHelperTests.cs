namespace GalNet.Assets.Tests;

public sealed class CryptoHelperTests
{
    private static readonly byte[] SampleData = "This is sensitive game data!"u8.ToArray();

    [Test]
    public void HashSHA256_Returns64HexChars()
    {
        var hash = CryptoHelper.HashSHA256(SampleData);
        Assert.That(hash, Has.Length.EqualTo(64));
        Assert.That(hash, Does.Match("^[0-9a-f]{64}$"));
    }

    [Test]
    public void HashSHA256_SameData_ReturnsSameHash()
    {
        var hash1 = CryptoHelper.HashSHA256(SampleData);
        var hash2 = CryptoHelper.HashSHA256(SampleData);
        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void HashSHA256_DifferentData_ReturnsDifferentHash()
    {
        var hash1 = CryptoHelper.HashSHA256("hello"u8.ToArray());
        var hash2 = CryptoHelper.HashSHA256("world"u8.ToArray());
        Assert.That(hash1, Is.Not.EqualTo(hash2));
    }

    [Test]
    public void HashSHA256_ReadOnlySpan_Works()
    {
        var hash = CryptoHelper.HashSHA256(new ReadOnlySpan<byte>(SampleData));
        Assert.That(hash, Has.Length.EqualTo(64));
    }

    [Test]
    public void HashSHA256_Stream_Works()
    {
        using var stream = new MemoryStream(SampleData);
        var hash = CryptoHelper.HashSHA256(stream);
        Assert.That(hash, Has.Length.EqualTo(64));
    }

    [Test]
    public void EncryptAES_DecryptAES_ReturnsOriginal()
    {
        const string password = "test-password-123";
        var encrypted = CryptoHelper.EncryptAES(SampleData, password);
        Assert.That(encrypted.Length, Is.GreaterThan(SampleData.Length));

        var decrypted = CryptoHelper.DecryptAES(encrypted, password);
        Assert.That(decrypted, Is.EqualTo(SampleData));
    }

    [Test]
    public void EncryptAES_SameData_WrongPassword_Fails()
    {
        const string password = "correct-password";
        const string wrongPassword = "wrong-password";

        var encrypted = CryptoHelper.EncryptAES(SampleData, password);
        Assert.That(() => CryptoHelper.DecryptAES(encrypted, wrongPassword),
            Throws.Exception);
    }

    [Test]
    public void EncryptAES_SameData_ProducesDifferentCiphertexts()
    {
        const string password = "password";

        var encrypted1 = CryptoHelper.EncryptAES(SampleData, password);
        var encrypted2 = CryptoHelper.EncryptAES(SampleData, password);
        // Each encryption has a unique salt and IV
        Assert.That(encrypted1, Is.Not.EqualTo(encrypted2));
    }

    [Test]
    public void EncryptAES_EmptyData_ReturnsSaltPlusIvPlusCiphertext()
    {
        const string password = "password";
        var encrypted = CryptoHelper.EncryptAES([], password);
        // salt(32) + IV(16) + ciphertext(should be non-empty due to PKCS7 padding)
        Assert.That(encrypted.Length, Is.EqualTo(32 + 16 + 16));
    }

    [Test]
    public void VerifyHash_ValidHash_ReturnsTrue()
    {
        var hash = CryptoHelper.HashSHA256(SampleData);
        Assert.That(CryptoHelper.VerifyHash(SampleData, hash), Is.True);
    }

    [Test]
    public void VerifyHash_InvalidHash_ReturnsFalse()
    {
        var hash = CryptoHelper.HashSHA256(SampleData);
        var tampered = SampleData.ToArray();
        tampered[0] ^= 1; // Flip a bit

        Assert.That(CryptoHelper.VerifyHash(tampered, hash), Is.False);
    }

    [Test]
    public void EncryptDecrypt_LargeData_Works()
    {
        var largeData = new byte[1024 * 1024]; // 1 MB
        new Random(42).NextBytes(largeData);

        const string password = "strong-password";
        var encrypted = CryptoHelper.EncryptAES(largeData, password);
        var decrypted = CryptoHelper.DecryptAES(encrypted, password);

        Assert.That(decrypted, Is.EqualTo(largeData));
    }
}
