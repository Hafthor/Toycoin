using System.Security.Cryptography;

namespace Toycoin;

public class Wallet : IDisposable {
    private readonly string _walletFile = "wallet.dat";
    private readonly byte[] _publicKey;
    private readonly RSACryptoServiceProvider _rsa;
    public ReadOnlySpan<byte> PublicKey => _publicKey.AsSpan();

    public Wallet(string walletFilename = null) {
        if (walletFilename != null) _walletFile = walletFilename;
        try {
            _rsa = new();
            byte[] privateKey;
            if (File.Exists(_walletFile)) {
                privateKey = File.ReadAllBytes(_walletFile);
                _rsa.ImportRSAPrivateKey(privateKey, out _);
            } else {
                privateKey = _rsa.ExportRSAPrivateKey();
                File.WriteAllBytes(_walletFile, privateKey);
            }
            Array.Clear(privateKey); // clear private key from memory for security
            _publicKey = _rsa.ExportRSAPublicKey();
        } finally {
            if (_publicKey == null) _rsa.Dispose(); // clear from memory for security
        }
    }
    
    public void SignData(ReadOnlySpan<byte> data, Span<byte> signature) =>
        _rsa.TrySignData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1, out _);

    public static bool VerifyData(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> publicKey) {
        using (RSACryptoServiceProvider rsa = new()) {
            rsa.ImportRSAPublicKey(publicKey, out _);
            return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
    }
    public override string ToString() => $"{Convert.ToHexString(_publicKey)}";

    public void Dispose() => _rsa.Dispose(); // clear from memory for security
}