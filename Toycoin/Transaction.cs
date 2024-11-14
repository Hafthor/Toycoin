using System.Diagnostics.Contracts;
using System.Security.Cryptography;

namespace Toycoin;

public class Transaction {
    public const int BinaryLength = 8 + 140 + 8 + 140 + 8 + 128; // 432 bytes
    public byte[] Data { get; }
    
    // we put Receiver and MicroAmount first to match the reward mini-transaction at the end
    public ulong BlockId => BitConverter.ToUInt64(Data.AsSpan()[..8]);
    public ReadOnlySpan<byte> Receiver => Data.AsSpan()[8..148];
    public ulong MicroAmount => BitConverter.ToUInt64(Data.AsSpan()[148..156]);
    public ReadOnlySpan<byte> Sender => Data.AsSpan()[156..296];
    public ulong MicroFee => BitConverter.ToUInt64(Data.AsSpan()[296..304]);
    public ReadOnlySpan<byte> ToBeSigned => Data.AsSpan()[..^128];
    private Span<byte> MySignature => Data.AsSpan()[^128..];
    public ReadOnlySpan<byte> Signature => MySignature;

    public Transaction(ulong blockId, ReadOnlySpan<byte> sender, ReadOnlySpan<byte> receiver, ulong microAmount, ulong microFee,
        ReadOnlySpan<byte> privateKey) {
        Contract.Assert(sender.Length == 140 && receiver.Length == 140, "Invalid public key length");
        Contract.Assert(privateKey.Length >= 600, "Invalid private key length");
        Data = [
            .. BitConverter.GetBytes(blockId),
            .. receiver,
            .. BitConverter.GetBytes(microAmount),
            .. sender,
            .. BitConverter.GetBytes(microFee),
            .. new byte[128], // signature
        ];
        using (RSACryptoServiceProvider rsa = new()) {
            rsa.ImportRSAPrivateKey(privateKey, out _);
            rsa.TrySignData(ToBeSigned, MySignature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1, out _);
        }
    }

    public Transaction(ReadOnlySpan<byte> buffer) : this(buffer.ToArray()) {
    }

    public Transaction(byte[] buffer) {
        Contract.Assert(buffer.Length == BinaryLength, "Invalid buffer length");
        Data = buffer;
        using (RSACryptoServiceProvider rsa = new()) {
            rsa.ImportRSAPublicKey(Sender, out _);
            var valid = rsa.VerifyData(ToBeSigned, Signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            Contract.Assert(valid, "Invalid signature");
        }
    }

    public override string ToString() =>
        $"{Convert.ToHexString(Sender)} -> {Convert.ToHexString(Receiver)} {MicroAmount}+{MicroFee}";
}