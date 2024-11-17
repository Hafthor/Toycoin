using System.Security.Cryptography;

namespace Toycoin;

public class Wallet : IDisposable {
    public const int PublicKeyLength = 140, SignatureLength = 128;
    private readonly string walletFile = "wallet.dat";
    private readonly byte[] publicKey;
    private readonly RSACryptoServiceProvider rsa = new(); // remember to dispose of this for security
    public void Dispose() => rsa.Dispose(); // clear from memory for security
    
    public ReadOnlySpan<byte> PublicKey => publicKey.AsSpan();

    public Wallet(string walletFilename = null) {
        try {
            if (walletFilename != null) walletFile = walletFilename;
            if (File.Exists(walletFile)) {
                var privateKey = File.ReadAllBytes(walletFile); // remember to clear from memory for security
                rsa.ImportRSAPrivateKey(privateKey, out _);
                Array.Clear(privateKey); // clear private key from memory for security
            } else {
                var privateKey = rsa.ExportRSAPrivateKey(); // remember to clear from memory for security
                File.WriteAllBytes(walletFile, privateKey);
                Array.Clear(privateKey); // clear private key from memory for security
            }
            publicKey = rsa.ExportRSAPublicKey();
        } finally {
            if (publicKey == null) rsa?.Dispose(); // clear from memory for security, but only if we did NOT succeed
        }
    }

    public void SignData(ReadOnlySpan<byte> data, Span<byte> signature) =>
        rsa.TrySignData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1, out _);

    public static bool VerifyData(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> publicKey) {
        using (RSACryptoServiceProvider localRsa = new()) {
            localRsa.ImportRSAPublicKey(publicKey, out _);
            return localRsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
    }

    public override string ToString() => $"{Convert.ToHexString(publicKey)}";
}