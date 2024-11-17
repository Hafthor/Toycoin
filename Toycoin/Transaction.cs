using System.Diagnostics.Contracts;

namespace Toycoin;

public class Transaction {
    public const int BinaryLength = sizeof(ulong) + Wallet.PublicKeyLength + 
                                    sizeof(ulong) + Wallet.PublicKeyLength + 
                                    sizeof(ulong) + Wallet.SignatureLength; // 432 bytes
    public byte[] Data { get; }

    // we put Receiver and MicroAmount first to match the reward mini-transaction at the end
    public ulong BlockId => BitConverter.ToUInt64(Data.AsSpan(0, sizeof(ulong)));
    private Span<byte> dataAtReceiver => Data.AsSpan(sizeof(ulong));
    public ReadOnlySpan<byte> Receiver => dataAtReceiver[..Wallet.PublicKeyLength];
    private Span<byte> dataAtMicroAmount => dataAtReceiver[Wallet.PublicKeyLength..];
    public ulong MicroAmount => BitConverter.ToUInt64(dataAtMicroAmount[..sizeof(ulong)]);
    private Span<byte> dataAtSender => dataAtMicroAmount[sizeof(ulong)..];
    public ReadOnlySpan<byte> Sender => dataAtSender[..Wallet.PublicKeyLength];
    private Span<byte> dataAtMicroFee => dataAtSender[Wallet.PublicKeyLength..];
    public ulong MicroFee => BitConverter.ToUInt64(dataAtMicroFee[..sizeof(ulong)]);
    private Span<byte> dataAtSignature => dataAtMicroFee[sizeof(ulong)..];
    public ReadOnlySpan<byte> ToBeSigned => Data.AsSpan()[..^Wallet.SignatureLength];
    private Span<byte> signature => Data.AsSpan()[^Wallet.SignatureLength..];
    public ReadOnlySpan<byte> Signature => signature;

    public Transaction(ulong blockId, ReadOnlySpan<byte> receiver, ulong microAmount, ulong microFee, Wallet wallet) {
        Data = [
            .. BitConverter.GetBytes(blockId),
            .. receiver,
            .. BitConverter.GetBytes(microAmount),
            .. wallet.PublicKey,
            .. BitConverter.GetBytes(microFee),
            .. new byte[Wallet.SignatureLength], // signature
        ];
        Contract.Assert(Data.Length == BinaryLength, "Invalid data length");
        Contract.Assert(dataAtSignature.Length == Wallet.SignatureLength, "Something wrong with the data layout");
        wallet.SignData(ToBeSigned, signature);
    }

    public Transaction(ReadOnlySpan<byte> buffer) : this(buffer.ToArray()) {
    }

    public Transaction(byte[] buffer) {
        Contract.Assert(buffer.Length == BinaryLength, "Invalid buffer length");
        Data = buffer;
        Contract.Assert(Wallet.VerifyData(ToBeSigned, Signature, Sender), "Invalid signature");
    }

    public override string ToString() =>
        $"{Convert.ToHexString(Sender)} -> {Convert.ToHexString(Receiver)} {MicroAmount}+{MicroFee}";
}