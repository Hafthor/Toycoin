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
            Console.WriteLine($"tx: {tx}");
            transactions.Add(tx);
        }

        Console.WriteLine("Loading...");
        var bc = new Blockchain(quiet: false);
        Console.WriteLine("Mining...");
        for (;;) { // mining loop
            // get the highest paying transactions
            var mineTxs = transactions.OrderByDescending(tx => tx.MicroFee).Take(bc.MaxTransactions).ToList();
            transactions = transactions.Except(mineTxs).ToList(); // remove the transactions we are mining for
            var startTime = Stopwatch.GetTimestamp();
            var block = bc.Mine(myPublicKey, mineTxs);
            var elapsed = Stopwatch.GetElapsedTime(startTime).TotalSeconds;
            var hashes = block.HashCount;
            Console.WriteLine("{0} {1:N0} {2:N3}s {3:N3}Mhps", bc.LastBlock, hashes, elapsed, hashes / elapsed / 1E6);
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

    private void UpdateBalances(Block block) =>
        UpdateBalances(block.ReadTransactions(), block.RewardPublicKey, block.TotalMicroRewardAmount);

    private void UpdateBalances(IEnumerable<Transaction> transactions, ReadOnlySpan<byte> rewardPublicKey,
        ulong totalMicroRewardAmount, bool justCheck = false) {
        checked {
            var rewardPublicKeyArray = rewardPublicKey.ToArray();
            var checkReward = MicroReward;
            // group transactions by sender and receiver with adds and subs
            Dictionary<byte[], ulong> adds = new(ByteArrayComparer.Instance), subs = new(ByteArrayComparer.Instance);
            foreach (var tx in transactions) {
                byte[] sender = tx.Sender.ToArray(), receiver = tx.Receiver.ToArray();
                adds[receiver] = adds.GetValueOrDefault(receiver, 0ul) + tx.MicroAmount;
                subs[sender] = subs.GetValueOrDefault(sender, 0ul) + tx.MicroAmount + tx.MicroFee;
                checkReward += tx.MicroFee;
            }
            Contract.Assert(checkReward == totalMicroRewardAmount, "Invalid total reward amount");
            adds[rewardPublicKeyArray] = adds.GetValueOrDefault(rewardPublicKeyArray, 0ul) + totalMicroRewardAmount;

            // update balances
            lock (_balances) {
                // perform adds first to avoid negative balances
                foreach (var (key, value) in subs)
                    Contract.Assert(_balances.GetValueOrDefault(key, 0ul) >= value, "Insufficient funds");
                if (justCheck) return;
                foreach (var (key, value) in adds)
                    _balances[key] = _balances.GetValueOrDefault(key, 0ul) + value;
                foreach (var (key, value) in subs)
                    _balances[key] -= value; // _balances MUST always have a value for key
            }
            if (!_quiet) {
                Console.WriteLine($"reward: {Convert.ToHexString(rewardPublicKey)} {totalMicroRewardAmount}");
                Console.WriteLine($"balances: {string.Join(", ",
                    _balances.Select(kv => $"{Convert.ToHexString(kv.Key)}={kv.Value}"))}");
            }
        }
    }

    public void ValidateTransactions(IEnumerable<Transaction> transactions, ulong totalMicroRewardAmount) =>
        UpdateBalances(transactions, Array.Empty<byte>(), totalMicroRewardAmount, justCheck: true);

    public Blockchain(bool quiet = true) {
        _quiet = quiet;
        if (!File.Exists(BlockchainFile)) return;
        foreach (var line in File.ReadAllLines(BlockchainFile)) {
            var ss = line.Split(' ').Select(Convert.FromHexString).ToArray(); // nonce, data, hash
            UpdateBalances(LastBlock = new(this, LastBlock, ss[1], ss[0], ss[2]));
            if (!quiet) Console.WriteLine(LastBlock);
        }
    }

    public Block Mine(ReadOnlySpan<byte> myPublicKey, List<Transaction> transactions) {
        LastBlock = new Block(this, LastBlock, transactions, myPublicKey);
        UpdateBalances(transactions, myPublicKey, MicroReward);
        File.AppendAllLines(BlockchainFile, [LastBlock.FileString()]);
        transactions.Clear();
        return LastBlock;
    }

    private int _previousSpinner = 0;

    public void Spinner() {
        if (_quiet) return;
        var t = DateTime.Now.Millisecond / 100;
        if (t == _previousSpinner) return;
        Console.Write("⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏"[_previousSpinner = t]);
        Console.Write('\b');
    }
}

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
            .. transactions.Select(tx => tx.Data).ToArray().Concat(),
            .. myPublicKey,
            ..BitConverter.GetBytes(totalMicroRewardAmount)
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

public class Transaction {
    public const int BinaryLength = 140 + 8 + 140 + 8 + 128; // 424 bytes
    public byte[] Data { get; }
    private Span<byte> MySignature => Data.AsSpan()[^128..];
    
    // we put Receiver and MicroAmount first to match the reward mini-transaction at the end
    public ReadOnlySpan<byte> Receiver => Data.AsSpan()[..140];
    public ulong MicroAmount => BitConverter.ToUInt64(Data.AsSpan()[140..148]);
    public ReadOnlySpan<byte> Sender => Data.AsSpan()[148..288];
    public ulong MicroFee => BitConverter.ToUInt64(Data.AsSpan()[288..296]);
    public ReadOnlySpan<byte> ToBeSigned => Data.AsSpan()[..^128];
    public ReadOnlySpan<byte> Signature => MySignature;

    public Transaction(ReadOnlySpan<byte> sender, ReadOnlySpan<byte> receiver, ulong microAmount, ulong microFee,
        ReadOnlySpan<byte> privateKey) {
        Contract.Assert(sender.Length == 140 && receiver.Length == 140, "Invalid public key length");
        Contract.Assert(privateKey.Length >= 600, "Invalid private key length");
        Data = [
            .. receiver,
            .. BitConverter.GetBytes(microAmount),
            .. sender,
            .. BitConverter.GetBytes(microFee),
            .. new byte[128] // signature
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
            Contract.Assert(
                rsa.VerifyData(ToBeSigned, Signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                "Invalid signature");
        }
    }

    public override string ToString() =>
        $"{Convert.ToHexString(Sender)} -> {Convert.ToHexString(Receiver)} {MicroAmount}+{MicroFee}";
}

public class Wallet : IDisposable {
    public const string WalletFile = "wallet.dat";
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

public class ByteArrayComparer : EqualityComparer<byte[]> {
    public static readonly ByteArrayComparer Instance = new();

    private ByteArrayComparer() { // don't instantiate - use static ByteArrayComparer.Instance
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
        var result = new byte[arrays.Sum(a => a.Length)];
        var offset = 0;
        foreach (var a in arrays) {
            Buffer.BlockCopy(a, 0, result, offset, a.Length);
            offset += a.Length;
        }
        return result;
    }

    public static bool IsLessThan(this byte[] first, byte[] second) => IsLessThan(first.AsSpan(), second.AsSpan());
    public static bool IsLessThan(this ReadOnlySpan<byte> first, byte[] second) => IsLessThan(first, second.AsSpan());
    public static bool IsLessThan(this byte[] first, ReadOnlySpan<byte> second) => IsLessThan(first.AsSpan(), second);

    public static bool IsLessThan(this ReadOnlySpan<byte> first, ReadOnlySpan<byte> second) {
        for (int i = 0, length = Math.Min(first.Length, second.Length); i < length; i++)
            if (first[i] != second[i])
                return first[i] < second[i];
        return true;
    }
}