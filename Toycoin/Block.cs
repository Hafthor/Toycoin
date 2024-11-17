using System.Diagnostics.Contracts;
using System.Security.Cryptography;

namespace Toycoin;

public class Block {
    public const int HashLength = 32, NonceLength = 32;

    public const int MinimumBinaryLength = HashLength + sizeof(ulong) + NonceLength +
                                           Wallet.PublicKeyLength + sizeof(ulong) + HashLength;
    public Block Previous { get; }

    // PreviousHash(32) + BlockId(8) + Nonce(32) + Transactions(424*n) + RewardPublicKey(140) + TotalMicroRewardAmount(8) + Hash(32)
    private byte[] data { get; }
    public ReadOnlySpan<byte> PreviousHash => data.AsSpan(0, HashLength);
    private Span<byte> dataAtBlockId => data.AsSpan(HashLength);
    private Span<byte> blockId => dataAtBlockId[..sizeof(ulong)];
    public ulong BlockId => BitConverter.ToUInt64(blockId);
    private Span<byte> dataAtNonce => dataAtBlockId[sizeof(ulong)..];
    private Span<byte> nonce => dataAtNonce[..NonceLength];
    public ReadOnlySpan<byte> Nonce => nonce;
    private Span<byte> dataAtTransaction => dataAtNonce[NonceLength..];
    public ReadOnlySpan<byte> TransactionData => dataAtTransaction[..^HashLength];
    private ReadOnlySpan<byte> toBeHashed => data.AsSpan()[..^HashLength];
    private Span<byte> hash => data.AsSpan()[^HashLength..];
    public ReadOnlySpan<byte> Hash => hash;
    public ReadOnlySpan<byte> Transactions => TransactionData[..^(Wallet.PublicKeyLength + sizeof(ulong))];
    public ReadOnlySpan<byte> RewardPublicKey =>
        TransactionData[^(Wallet.PublicKeyLength + sizeof(ulong))..^sizeof(ulong)];
    public ulong TotalMicroRewardAmount => BitConverter.ToUInt64(TransactionData[^sizeof(ulong)..]);

    public Block(Blockchain bc, IReadOnlyList<Transaction> transactions, ReadOnlySpan<byte> myPublicKey) :
        this(bc, MakeData(bc, transactions, myPublicKey)) {
    }

    private static byte[] MakeData(Blockchain bc, IReadOnlyList<Transaction> transactions,
        ReadOnlySpan<byte> myPublicKey) {
        var totalMicroRewardAmount = transactions.Aggregate(bc.MicroReward, (sum, tx) => {
            checked {
                return sum + tx.MicroFee;
            }
        });
        bc.ValidateTransactions(transactions, myPublicKey, totalMicroRewardAmount);

        int bufferSize = transactions.Count * Transaction.BinaryLength + Wallet.PublicKeyLength + sizeof(ulong);
        byte[] buffer = new byte[bufferSize];
        int ptr = 0;
        foreach (var tx in transactions) {
            tx.Data.CopyTo(buffer.AsSpan()[ptr..]);
            ptr += Transaction.BinaryLength;
        }
        myPublicKey.CopyTo(buffer.AsSpan()[ptr..]);
        ptr += Wallet.PublicKeyLength;
        BitConverter.TryWriteBytes(buffer.AsSpan()[ptr..], totalMicroRewardAmount);
        ptr += sizeof(ulong);
        Contract.Assert(ptr == buffer.Length, "Did not fill buffer correctly");
        return buffer;
    }

    public Block(Blockchain bc, ReadOnlySpan<byte> data, byte[] nonce = null, byte[] hash = null) {
        Contract.Assert(bc != null, "Missing blockchain");
        Contract.Assert(nonce == null || nonce.Length == NonceLength, "Invalid nonce length");
        Contract.Assert(hash == null || hash.Length == HashLength, "Invalid hash length");
        Previous = bc.LastBlock;
        var previousHash = Previous == null ? new byte[HashLength] : Previous.Hash;
        var blockId = Previous == null ? 0ul : Previous.BlockId + 1;
        this.data = [
            .. previousHash,
            .. BitConverter.GetBytes(blockId),
            .. nonce ?? new byte[NonceLength],
            .. data,
            .. hash ?? new byte[HashLength]
        ];
        Contract.Assert(dataAtTransaction.Length == TransactionData.Length + HashLength, "Invalid data length");
        Contract.Assert(data.Length % Transaction.BinaryLength == Wallet.PublicKeyLength + sizeof(ulong),
            "Invalid data length");
        VerifyBlockData(bc, RewardPublicKey);
        if (nonce == null) new Random().NextBytes(this.nonce);
        Contract.Assert(hash == null || Hash.SequenceCompareTo(bc.Difficulty) < 0 && Hash.SequenceEqual(hash),
            "Invalid hash");
    }

    public Block IncrementAndHash() {
        for (int i = 0; i < Nonce.Length && ++nonce[i] == 0; i++) ; // increment nonce
        SHA256.TryHashData(toBeHashed, hash, out _);
        return this;
    }

    public string FileString() =>
        string.Join(" ", Convert.ToHexString(Nonce), Convert.ToHexString(TransactionData), Convert.ToHexString(Hash));

    public override string ToString() => $"nonce={Convert.ToHexString(Nonce)} hash={Convert.ToHexString(Hash)}";

    public IEnumerable<Transaction> ReadTransactions() {
        int txCount = TransactionData.Length / Transaction.BinaryLength,
            remainder = TransactionData.Length % Transaction.BinaryLength;
        Contract.Assert(remainder == Wallet.PublicKeyLength + sizeof(ulong), "expected reward transaction at end");
        for (int i = 0, si = 0; i < txCount; i++)
            yield return new(TransactionData[si..(si += Transaction.BinaryLength)]);
    }

    private void VerifyBlockData(Blockchain bc, ReadOnlySpan<byte> myPublicKey) =>
        bc.ValidateTransactions(ReadTransactions(), myPublicKey, TotalMicroRewardAmount);
}