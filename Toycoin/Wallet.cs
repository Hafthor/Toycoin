using System.Security.Cryptography;

namespace Toycoin;

public class Wallet : IDisposable {
    private readonly string _walletFile = "wallet.dat";
    private readonly byte[] _privateKey, _publicKey;
    public ReadOnlySpan<byte> PublicKey => _publicKey.AsSpan();

    public Wallet(string walletFilename = null) {
        if (walletFilename != null) _walletFile = walletFilename;
        using (RSACryptoServiceProvider rsa = new()) {
            if (File.Exists(_walletFile)) {
                _privateKey = File.ReadAllBytes(_walletFile);
                rsa.ImportRSAPrivateKey(_privateKey, out _);
            } else {
                _privateKey = rsa.ExportRSAPrivateKey();
                File.WriteAllBytes(_walletFile, _privateKey);
            }
            _publicKey = rsa.ExportRSAPublicKey();
        }
    }

    public Transaction CreateTransaction(ulong blockId, ReadOnlySpan<byte> receiver, ulong microAmount, ulong microFee) =>
        new(blockId, PublicKey, receiver, microAmount, microFee, _privateKey);

    public override string ToString() => $"{Convert.ToHexString(_publicKey)}";

    public void Dispose() => Array.Clear(_privateKey); // clear private key from memory for security
}