using System.Security.Cryptography;

namespace Toycoin;

public class Wallet : IDisposable {
    private const string WalletFile = "wallet.dat";
    private readonly byte[] _data;
    private Span<byte> MyPrivateKey => _data.AsSpan()[140..];
    public ReadOnlySpan<byte> PublicKey => _data.AsSpan()[..140];
    public ReadOnlySpan<byte> PrivateKey => MyPrivateKey;

    public Wallet() {
        if (File.Exists(WalletFile)) {
            _data = File.ReadAllBytes(WalletFile);
        } else {
            using (RSACryptoServiceProvider rsa = new()) {
                byte[] publicKey = rsa.ExportRSAPublicKey(), privateKey = rsa.ExportRSAPrivateKey();
                _data = [.. publicKey, .. privateKey];
                Array.Clear(privateKey); // clear copy of private key from memory for security
            }
            File.WriteAllBytes(WalletFile, _data);
        }
    }

    public Transaction CreateTransaction(ReadOnlySpan<byte> receiver, ulong microAmount, ulong microFee) =>
        new(PublicKey, receiver, microAmount, microFee, PrivateKey);

    public override string ToString() => $"{Convert.ToHexString(PublicKey)}";

    public void Dispose() => MyPrivateKey.Clear(); // clear private key from memory for security
}