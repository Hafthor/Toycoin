using System.Security.Cryptography;

namespace Toycoin;

public class Wallet : IDisposable {
    public const int PublicKeyLength = 140, SignatureLength = 128;
    
    public static bool VerifyData(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> publicKey) {
        using (RSACryptoServiceProvider localRsa = new()) {
            localRsa.ImportRSAPublicKey(publicKey, out _);
            return localRsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
    }

    private readonly RSACryptoServiceProvider rsa = new(); // remember to dispose of this for security
    private readonly byte[] publicKey;
    public ReadOnlySpan<byte> PublicKey => publicKey.AsSpan();

    public Wallet(string walletFilename = null) {
        byte[] privateKey = null;
        try {
            string walletFile = walletFilename ?? "wallet.dat";
            if (File.Exists(walletFile)) {
                privateKey = File.ReadAllBytes(walletFile); // remember to clear from memory for security
                rsa.ImportRSAPrivateKey(privateKey, out _);
            } else {
                privateKey = rsa.ExportRSAPrivateKey(); // remember to clear from memory for security
                File.WriteAllBytes(walletFile, privateKey);
            }
            publicKey = rsa.ExportRSAPublicKey();
        } finally {
            if (privateKey is not null) Array.Clear(privateKey); // clear private key from memory for security
            if (publicKey is null) rsa?.Dispose(); // clear from memory for security, but only if we did NOT succeed
        }
    }

    public void SignData(ReadOnlySpan<byte> data, Span<byte> signature) =>
        rsa.TrySignData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1, out _);
    
    public override string ToString() => $"{Convert.ToHexString(publicKey)}";
    
    public void Dispose() => rsa.Dispose(); // clear from memory for security
}