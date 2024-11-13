using System.Security.Cryptography;

namespace Toycoin;

public class Wallet : IDisposable {
    private const string WalletFile = "wallet.dat";
    private readonly byte[] _privateKey, _publicKey;
    public ReadOnlySpan<byte> PublicKey => _publicKey.AsSpan();

    public Wallet() {
        using (RSACryptoServiceProvider rsa = new()) {
            if (File.Exists(WalletFile)) {
                _privateKey = File.ReadAllBytes(WalletFile);
                rsa.ImportRSAPrivateKey(_privateKey, out _);
            } else {
                _privateKey = rsa.ExportRSAPrivateKey();
                File.WriteAllBytes(WalletFile, _privateKey);
            }
            _publicKey = rsa.ExportRSAPublicKey();
        }
    }

    public Transaction CreateTransaction(ReadOnlySpan<byte> receiver, ulong microAmount, ulong microFee) =>
        new(PublicKey, receiver, microAmount, microFee, _privateKey);

    public override string ToString() => $"{Convert.ToHexString(_publicKey)}";

    public void Dispose() => Array.Clear(_privateKey); // clear private key from memory for security
}