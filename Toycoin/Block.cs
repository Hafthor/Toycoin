using System.Diagnostics.Contracts;
using System.Security.Cryptography;

namespace Toycoin;

public class Block {
    public Block Previous { get; }

    // PreviousHash(32) + BlockId(8) + Nonce(32) + Transactions(424*n) + RewardPublicKey(140) + TotalMicroRewardAmount(8) + Hash(32)
    private byte[] data { get; }
    public ReadOnlySpan<byte> PreviousHash => data.AsSpan()[..32];
    private Span<byte> blockId => data.AsSpan()[32..40];
    public ulong BlockId => BitConverter.ToUInt64(blockId);
    private Span<byte> nonce => data.AsSpan()[40..72];
    public ReadOnlySpan<byte> Nonce => nonce;
    public ReadOnlySpan<byte> TransactionData => data.AsSpan()[72..^32];
    private ReadOnlySpan<byte> toBeHashed => data.AsSpan()[..^32];
    private Span<byte> hash => data.AsSpan()[^32..];
    public ReadOnlySpan<byte> Hash => hash;

    public ReadOnlySpan<byte> Transactions => TransactionData[..^148];
    public ReadOnlySpan<byte> RewardPublicKey => TransactionData[^148..^8];
    public ulong TotalMicroRewardAmount => BitConverter.ToUInt64(TransactionData[^8..]);

    public Block(Blockchain bc, Block previous, IReadOnlyList<Transaction> transactions,
        ReadOnlySpan<byte> myPublicKey) :
        this(bc, previous, MakeData(bc, transactions, myPublicKey)) {
    }

    private static byte[] MakeData(Blockchain bc, IReadOnlyList<Transaction> transactions,
        ReadOnlySpan<byte> myPublicKey) {
        var totalMicroRewardAmount = transactions.Aggregate(bc.MicroReward, (sum, tx) => {
            checked {
                return sum + tx.MicroFee;
            }
        });
        bc.ValidateTransactions(transactions, myPublicKey, totalMicroRewardAmount);

        int bufferSize = transactions.Count * Transaction.BinaryLength + 140 + 8;
        byte[] buffer = new byte[bufferSize];
        int ptr = 0;
        foreach (var tx in transactions) {
            tx.Data.CopyTo(buffer.AsSpan()[ptr..]);
            ptr += Transaction.BinaryLength;
        }
        myPublicKey.CopyTo(buffer.AsSpan()[ptr..]);
        ptr += 140;
        BitConverter.TryWriteBytes(buffer.AsSpan()[ptr..], totalMicroRewardAmount);
        return buffer;
    }

    public Block(Blockchain bc, Block previous, ReadOnlySpan<byte> data, byte[] nonce = null, byte[] hash = null) {
        Contract.Assert(bc != null, "Missing blockchain");
        Contract.Assert(nonce == null || nonce.Length == 32, "Invalid nonce length");
        Contract.Assert(hash == null || hash.Length == 32, "Invalid hash length");
        Contract.Assert(data.Length % Transaction.BinaryLength == 140 + 8, "Invalid data length");
        Previous = previous;
        var previousHash = previous == null ? new byte[32] : previous.Hash;
        var blockId = previous == null ? 0ul : previous.BlockId + 1;
        this.data = [
            .. previousHash,
            .. BitConverter.GetBytes(blockId),
            .. nonce ?? new byte[32],
            .. data,
            .. hash ?? new byte[32]
        ];
        VerifyBlockData(bc, data[^148..^8]);
        if (nonce == null) new Random().NextBytes(this.nonce);
        Contract.Assert(hash == null || Hash.IsLessThan(bc.Difficulty) && Hash.SequenceEqual(hash), "Invalid hash");
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
        Contract.Assert(remainder == 140 + 8, "expected reward transaction at end");
        for (int i = 0, si = 0; i < txCount; i++)
            yield return new(TransactionData[si..(si += Transaction.BinaryLength)]);
    }

    private void VerifyBlockData(Blockchain bc, ReadOnlySpan<byte> myPublicKey) =>
        bc.ValidateTransactions(ReadTransactions(), myPublicKey, TotalMicroRewardAmount);
}