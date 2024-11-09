using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Security.Cryptography;

namespace Toycoin;

public static class Program {
    public static void Main() {
        ReadOnlySpan<byte> myPublicKey;
        List<Transaction> transactions = [];
        using (Wallet wallet = new()) {
            myPublicKey = wallet.PublicKey;
            var tx = wallet.CreateTransaction(myPublicKey, 0, 0); // create a dummy transaction
            Console.WriteLine("tx: " + Convert.ToHexString(tx.Data));
            transactions.Add(tx);
        }

        Console.WriteLine("Loading...");
        var bc = new Blockchain(quiet: false);
        Console.WriteLine("Mining...");
        for (;;) { // mining loop
            var sw = Stopwatch.GetTimestamp();
            var txs = transactions.OrderByDescending(tx => tx.MicroFee).Take(bc.MaxTransactions).ToList();
            transactions = transactions.Except(txs).ToList();
            bc.Mine(myPublicKey, txs);
            var elapsed = Stopwatch.GetElapsedTime(sw).TotalSeconds;
            var hc = bc.LastBlock.HashCount;
            Console.WriteLine("{0} {1:N0} {2:N3}s {3:N3}Mhps", bc.LastBlock, hc, elapsed, hc / elapsed / 1E6);
        }
    }
}

public class Blockchain {
    public const string BlockchainFile = "blockchain.txt";
    public Block LastBlock { get; private set; }
    public byte[] Difficulty { get; } = [0, 0, 0]; // 3 leading zeros
    public ulong MicroReward { get; } = 1_000_000; // 1 toycoin
    public int MaxTransactions { get; } = 10;
    
    private readonly bool _quiet;

    private readonly Dictionary<byte[], ulong> _balances = new(ByteArrayComparer.Instance);

    private void UpdateBalances(Block block) {
        UpdateBalances(block.ReadTransactions(), block.RewardPublicKey, block.RewardAmount);
    }

    private void UpdateBalances(IEnumerable<Transaction> transactions, ReadOnlySpan<byte> rewardPublicKey,
        ulong rewardAmount) {
        checked {
            foreach (var tx in transactions) {
                if (!_quiet)
                    Console.WriteLine($"tx: {Convert.ToHexString(tx.Sender)} -> {Convert.ToHexString(tx.Receiver)} {
                        tx.MicroAmount}+{tx.MicroFee}");
                byte[] sender = tx.Sender.ToArray(), receiver = tx.Receiver.ToArray();
                _balances[sender] = _balances.GetValueOrDefault(sender, 0ul) - tx.MicroAmount - tx.MicroFee;
                _balances[receiver] = _balances.GetValueOrDefault(receiver, 0ul) + tx.MicroAmount;
            }
            byte[] rewardPublicKeyArray = rewardPublicKey.ToArray();
            _balances[rewardPublicKeyArray] = _balances.GetValueOrDefault(rewardPublicKeyArray, 0ul) + rewardAmount;
            if (!_quiet) {
                Console.WriteLine($"reward: {Convert.ToHexString(rewardPublicKey)} {rewardAmount}");
                Console.WriteLine($"balances: {string.Join(", ",
                    _balances.Select(kv => $"{Convert.ToHexString(kv.Key)}={kv.Value}"))}");
            }
        }
    }

    public void ValidateTransactions(IEnumerable<Transaction> transactions, ulong reward) {
        // pool withdrawals by sender so we can check that they have sufficient funds to cover their transactions
        Dictionary<byte[], ulong> withdrawals = new(ByteArrayComparer.Instance);
        ulong actualReward = MicroReward;
        int count = 0;
        checked {
            foreach (var tx in transactions) {
                var sender = tx.Sender.ToArray();
                withdrawals[sender] = withdrawals.GetValueOrDefault(sender, 0ul) + tx.MicroAmount + tx.MicroFee;
                actualReward += tx.MicroFee;
                count++;
            }
        }
        Contract.Assert(count <= MaxTransactions, "Too many transactions");
        Contract.Assert(reward == actualReward, "Invalid reward amount");
        foreach (var (sender, amount) in withdrawals)
            Contract.Assert(_balances.GetValueOrDefault(sender, 0ul) >= amount, "Insufficient funds");
    }

    public Blockchain(bool quiet = true) {
        _quiet = quiet;
        if (File.Exists(BlockchainFile)) {
            foreach (var line in File.ReadAllLines(BlockchainFile)) {
                var ss = line.Split(' ').Select(Convert.FromHexString).ToArray(); // nonce, data, hash
                UpdateBalances(LastBlock = new(this, LastBlock, ss[1], ss[0], ss[2]));
                if (!quiet) Console.WriteLine(LastBlock);
            }
        }
    }

    public void Mine(ReadOnlySpan<byte> myPublicKey, List<Transaction> transactions) {
        LastBlock = new Block(this, LastBlock, transactions, myPublicKey);
        UpdateBalances(transactions, myPublicKey, MicroReward);
        File.AppendAllLines(BlockchainFile, [LastBlock.FileString()]);
        transactions.Clear();
    }
}

public class Block {
    public Block Previous { get; }
    public byte[] BlockData { get; } // PreviousHash + Nonce + Transactions + RewardPublicKey + RewardAmount + Hash
    private ReadOnlySpan<byte> ToBeHashed => BlockData.AsSpan()[..^32];
    public ReadOnlySpan<byte> PreviousHash => BlockData.AsSpan()[..32];
    private Span<byte> MyNonce => BlockData.AsSpan()[32..64];
    public ReadOnlySpan<byte> Nonce => MyNonce;
    public ReadOnlySpan<byte> Data => BlockData.AsSpan()[64..^32];
    private Span<byte> MyHash => BlockData.AsSpan()[^32..];
    public ReadOnlySpan<byte> Hash => MyHash;
    public ReadOnlySpan<byte> Transactions => Data[..^148];
    public ReadOnlySpan<byte> RewardPublicKey => Data[^148..^8];
    public ulong RewardAmount => BitConverter.ToUInt64(Data[^8..]);
    public int HashCount { get; }

    public Block(Blockchain bc, Block previous, IList<Transaction> transactions, ReadOnlySpan<byte> myPublicKey) :
        this(bc, previous, MakeData(bc, transactions, myPublicKey)) {
    }

    private static byte[] MakeData(Blockchain bc, IList<Transaction> transactions, ReadOnlySpan<byte> myPublicKey) {
        ulong reward = transactions.Aggregate(bc.MicroReward, (sum, tx) => {
            checked {
                return sum + tx.MicroFee;
            }
        });
        bc.ValidateTransactions(transactions, reward);
        return [
            .. transactions.Select(tx => tx.Data).ToArray().Concat(),
            .. myPublicKey,
            ..BitConverter.GetBytes(reward)
        ];
    }

    public Block(Blockchain bc, Block previous, ReadOnlySpan<byte> data, byte[] nonce = null, byte[] hash = null) {
        Contract.Assert(bc != null, "Missing blockchain");
        Contract.Assert(nonce == null || nonce.Length == 32, "Invalid nonce length");
        Contract.Assert(hash == null || hash.Length == 32, "Invalid hash length");
        Contract.Assert(data.Length % Transaction.BinaryLength == 140 + 8, "Invalid data length");
        Previous = previous;
        ReadOnlySpan<byte> previousHash = previous == null ? new byte[32] : previous.Hash;
        BlockData = [
            .. previousHash,
            .. nonce ?? new byte[32],
            .. data,
            .. hash ?? new byte[32]
        ];
        VerifyBlockData(bc);
        if (nonce == null) new Random().NextBytes(MyNonce);
        if (hash == null) {
            do { // mine loop - increment nonce
                for (int i = 0; i < Nonce.Length && ++MyNonce[i] == 0; i++) ;
                SHA256.TryHashData(ToBeHashed, MyHash, out _);
                HashCount++;
            } while (!Hash.IsLessThan(bc.Difficulty));
        } else {
            Contract.Assert(hash.IsLessThan(bc.Difficulty), "Invalid hash");
            hash = SHA256.HashData(ToBeHashed);
            Contract.Assert(Hash.SequenceEqual(hash), "Invalid hash");
        }
    }

    public string FileString() =>
        string.Join(" ", Convert.ToHexString(Nonce), Convert.ToHexString(Data), Convert.ToHexString(Hash));

    public override string ToString() => $"nonce={Convert.ToHexString(Nonce)} hash={Convert.ToHexString(Hash)}";

    public IEnumerable<Transaction> ReadTransactions() {
        int txCount = Data.Length / Transaction.BinaryLength, remainder = Data.Length % Transaction.BinaryLength;
        Contract.Assert(remainder == 140 + 8, "expected reward transaction at end");
        for (int i = 0, si = 0; i < txCount; i++)
            yield return new(Data[si..(si += Transaction.BinaryLength)]);
    }
    
    private void VerifyBlockData(Blockchain bc) => bc.ValidateTransactions(ReadTransactions(), RewardAmount);
}

public class Transaction {
    public const int BinaryLength = 140 + 140 + 8 + 8 + 128; // 424 bytes
    public byte[] Data { get; }
    public ReadOnlySpan<byte> Sender => Data.AsSpan()[..140];
    public ReadOnlySpan<byte> Receiver => Data.AsSpan()[140..280];
    public ulong MicroAmount => BitConverter.ToUInt64(Data.AsSpan()[280..288]);
    public ulong MicroFee => BitConverter.ToUInt64(Data.AsSpan()[288..296]);
    public ReadOnlySpan<byte> Signature => Data.AsSpan()[296..];

    public Transaction(ReadOnlySpan<byte> sender, ReadOnlySpan<byte> receiver, ulong microAmount, ulong microFee,
        ReadOnlySpan<byte> privateKey) {
        Contract.Assert(sender.Length == 140 && receiver.Length == 140, "Invalid public key length");
        Contract.Assert(privateKey.Length >= 600, "Invalid private key length");
        Data = [
            .. sender, .. receiver, .. BitConverter.GetBytes(microAmount), .. BitConverter.GetBytes(microFee),
            .. new byte[128]
        ];
        using (RSACryptoServiceProvider rsa = new()) {
            rsa.ImportRSAPrivateKey(privateKey, out _);
            var signature = rsa.SignData(Data.AsSpan()[..^128], HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            signature.CopyTo(Data.AsSpan()[^128..]);
        }
    }

    public Transaction(ReadOnlySpan<byte> buffer) {
        Contract.Assert(buffer.Length == BinaryLength, "Invalid buffer length");
        Data = buffer.ToArray();
        using (RSACryptoServiceProvider rsa = new()) {
            rsa.ImportRSAPublicKey(Sender, out _);
            Contract.Assert(
                rsa.VerifyData(buffer[..^128], Signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                "Invalid signature");
        }
    }
}

public class Wallet : IDisposable {
    public const string WalletFile = "wallet.dat";
    private readonly byte[] _data;
    public ReadOnlySpan<byte> PublicKey => _data.AsSpan()[..140];
    private Span<byte> MyPrivateKey => _data.AsSpan()[140..];
    public ReadOnlySpan<byte> PrivateKey => MyPrivateKey;

    public Wallet() {
        if (File.Exists(WalletFile)) {
            _data = File.ReadAllBytes(WalletFile);
        } else {
            using (RSACryptoServiceProvider rsa = new()) {
                var publicKey = rsa.ExportRSAPublicKey();
                var privateKey = rsa.ExportRSAPrivateKey();
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

public class ByteArrayComparer : EqualityComparer<byte[]> {
    public static readonly ByteArrayComparer Instance = new();

    private ByteArrayComparer() {
    }

    public override bool Equals(byte[] first, byte[] second) {
        if (first == null || second == null) return first == second;
        return ReferenceEquals(first, second) || first.Length == second.Length && first.SequenceEqual(second);
    }

    public override int GetHashCode(byte[] obj) {
        ArgumentNullException.ThrowIfNull(obj);
        return obj.Length;
    }
}

public static class Extensions {
    public static byte[] Concat(this IList<byte[]> arrays) {
        if (arrays.Count == 0) return [];
        if (arrays.Count == 1) return arrays[0];
        byte[] result = new byte[arrays.Sum(a => a.Length)];
        int offset = 0;
        foreach(var a in arrays) {
            Buffer.BlockCopy(a, 0, result, offset, a.Length);
            offset += a.Length;
        }
        return result;
    }

    public static bool IsLessThan(this byte[] first, byte[] second) => IsLessThan(first.AsSpan(), second.AsSpan());
    public static bool IsLessThan(this ReadOnlySpan<byte> first, byte[] second) => IsLessThan(first, second.AsSpan());
    public static bool IsLessThan(this byte[] first, ReadOnlySpan<byte> second) => IsLessThan(first.AsSpan(), second);
    
    public static bool IsLessThan(this ReadOnlySpan<byte> first, ReadOnlySpan<byte> second) {
        int length = Math.Min(first.Length, second.Length);
        for (int i = 0; i < length; i++) {
            int diff = (int)first[i] - (int)second[i];
            if (diff < 0) return true;
            if (diff > 0) return false;
        }
        return true;
    }
}