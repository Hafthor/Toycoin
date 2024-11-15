using System.Security.Cryptography;

namespace Toycoin;

public class Wallet : IDisposable {
    private readonly string walletFile = "wallet.dat";
    private readonly byte[] publicKey;
    private readonly RSACryptoServiceProvider rsa;
    public ReadOnlySpan<byte> PublicKey => publicKey.AsSpan();

    public Wallet(string walletFilename = null) {
        if (walletFilename != null) walletFile = walletFilename;
        try {
            rsa = new();
            byte[] privateKey;
            if (File.Exists(walletFile)) {
                privateKey = File.ReadAllBytes(walletFile);
                rsa.ImportRSAPrivateKey(privateKey, out _);
            } else {
                privateKey = rsa.ExportRSAPrivateKey();
                File.WriteAllBytes(walletFile, privateKey);
            }
            Array.Clear(privateKey); // clear private key from memory for security
            publicKey = rsa.ExportRSAPublicKey();
        } finally {
            if (publicKey == null) rsa?.Dispose(); // clear from memory for security, but only if we didn't succeed
        }
    }
    
    public void SignData(ReadOnlySpan<byte> data, Span<byte> signature) =>
        rsa.TrySignData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1, out _);

    public static bool VerifyData(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> publicKey) {
        using (RSACryptoServiceProvider rsa = new()) {
            rsa.ImportRSAPublicKey(publicKey, out _);
            return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
    }
    public override string ToString() => $"{Convert.ToHexString(publicKey)}";

    public void Dispose() => rsa.Dispose(); // clear from memory for security
}