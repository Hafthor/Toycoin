using System.Diagnostics.Contracts;
using System.Security.Cryptography;

namespace Toycoin;

public class Block {
    public Block Previous { get; }

    // PreviousHash(32) + Nonce(32) + Transactions(424*n) + RewardPublicKey(140) + TotalMicroRewardAmount(8) + Hash(32)
    public byte[] Data { get; }
    private Span<byte> MyNonce => Data.AsSpan()[32..64];
    private Span<byte> MyHash => Data.AsSpan()[^32..];
    private ReadOnlySpan<byte> ToBeHashed => Data.AsSpan()[..^32];
    public ReadOnlySpan<byte> PreviousHash => Data.AsSpan()[..32];
    public ReadOnlySpan<byte> Nonce => MyNonce;
    public ReadOnlySpan<byte> TransactionData => Data.AsSpan()[64..^32];
    public ReadOnlySpan<byte> Hash => MyHash;
    public ReadOnlySpan<byte> Transactions => TransactionData[..^148];
    public ReadOnlySpan<byte> RewardPublicKey => TransactionData[^148..^8];
    public ulong TotalMicroRewardAmount => BitConverter.ToUInt64(TransactionData[^8..]);
    public int HashCount { get; }

    public Block(Blockchain bc, Block previous, IList<Transaction> transactions, ReadOnlySpan<byte> myPublicKey) :
        this(bc, previous, MakeData(bc, transactions, myPublicKey)) {
    }

    private static byte[] MakeData(Blockchain bc, IList<Transaction> transactions, ReadOnlySpan<byte> myPublicKey) {
        var totalMicroRewardAmount = transactions.Aggregate(bc.MicroReward, (sum, tx) => {
            checked {
                return sum + tx.MicroFee;
            }
        });
        bc.ValidateTransactions(transactions, totalMicroRewardAmount);
        return [
            .. transactions.Select(tx => tx.Data).ToArray().Coalesce(),
            .. myPublicKey,
            ..BitConverter.GetBytes(totalMicroRewardAmount),
        ];
    }

    public Block(Blockchain bc, Block previous, ReadOnlySpan<byte> data, byte[] nonce = null, byte[] hash = null) {
        Contract.Assert(bc != null, "Missing blockchain");
        Contract.Assert(nonce == null || nonce.Length == 32, "Invalid nonce length");
        Contract.Assert(hash == null || hash.Length == 32, "Invalid hash length");
        Contract.Assert(data.Length % Transaction.BinaryLength == 140 + 8, "Invalid data length");
        Previous = previous;
        var previousHash = previous == null ? new byte[32] : previous.Hash;
        Data = [
            .. previousHash,
            .. nonce ?? new byte[32],
            .. data,
            .. hash ?? new byte[32]
        ];
        VerifyBlockData(bc);
        if (nonce == null) new Random().NextBytes(MyNonce);
        SHA256.TryHashData(ToBeHashed, MyHash, out _);
        if (hash == null) {
            for (int i; !Hash.IsLessThan(bc.Difficulty); HashCount++) { // mine loop
                for (i = 0; i < Nonce.Length && ++MyNonce[i] == 0; i++) ; // increment nonce
                if (i > 1) bc.Spinner();
                SHA256.TryHashData(ToBeHashed, MyHash, out _);
            }
        } else {
            Contract.Assert(Hash.IsLessThan(bc.Difficulty) && Hash.SequenceEqual(hash), "Invalid hash");
        }
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

    private void VerifyBlockData(Blockchain bc) => bc.ValidateTransactions(ReadTransactions(), TotalMicroRewardAmount);
}