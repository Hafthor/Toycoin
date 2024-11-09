using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Security.Cryptography;

namespace Toycoin;

public static class Program {
    public static void Main() {
        byte[] myPublicKey;
        List<Transaction> transactions = [];
        using (Wallet wallet = new()) {
            myPublicKey = wallet.PublicKey;
            var tx = wallet.CreateTransaction(myPublicKey, 0, 0);
            Console.WriteLine("tx: " + Convert.ToHexString(tx.Buffer));
            transactions.Add(tx);
        }

        Console.WriteLine("Loading...");
        var bc = new Blockchain(quiet: false);
        Console.WriteLine("Mining...");
        for (;;) { // mining loop
            var sw = Stopwatch.GetTimestamp();
            bc.Mine(myPublicKey, transactions);
            var elapsed = Stopwatch.GetElapsedTime(sw).TotalSeconds;
            transactions.Clear();
            var hc = bc.LastBlock.HashCount;
            Console.WriteLine("{0} {1:N0} {2:N3}s {3:N3}Mhps", bc.LastBlock, hc, elapsed, hc / elapsed / 1E6);
        }
    }
}

public class Blockchain {
    public const string BlockchainFile = "blockchain.txt";
    public Block LastBlock { get; private set; }
    public byte[] Difficulty { get; } = [0, 0, 0]; // 3 leading zeros

    private readonly Dictionary<byte[], ulong> _balances = new(ByteArrayComparer.Instance);

    private void UpdateBalances(Block block) {
        (List<Transaction> transactions, byte[] rewardPublicKey, ulong rewardAmount) = block.ReadData();
        UpdateBalances(transactions, rewardPublicKey, rewardAmount);
    }

    private void UpdateBalances(List<Transaction> transactions, byte[] rewardPublicKey, ulong rewardAmount) {
        checked {
            foreach (var tx in transactions) {
                _balances[tx.Sender] = _balances.GetValueOrDefault(tx.Sender, 0ul) - tx.MicroAmount - tx.MicroFee;
                _balances[tx.Receiver] = _balances.GetValueOrDefault(tx.Receiver, 0ul) + tx.MicroAmount;
            }
            _balances[rewardPublicKey] = _balances.GetValueOrDefault(rewardPublicKey, 0ul) + rewardAmount;
        }
    }

    public void ValidateTransactions(IList<Transaction> transactions, ulong reward) {
        ulong actualReward = transactions.Aggregate(Transaction.MicroReward, (sum, tx) => {
            checked {
                return sum + tx.MicroFee;
            }
        });
        Contract.Assert(reward == actualReward, "Invalid reward amount");

        // pool withdrawals by sender so we can check that they have sufficient funds to cover their transactions
        Dictionary<byte[], ulong> withdrawals = new(ByteArrayComparer.Instance);
        checked {
            foreach (var tx in transactions)
                withdrawals[tx.Sender] = withdrawals.GetValueOrDefault(tx.Sender, 0ul) + tx.MicroAmount + tx.MicroFee;
        }
        foreach (var (sender, amount) in withdrawals)
            Contract.Assert(_balances.GetValueOrDefault(sender, 0ul) >= amount, "Insufficient funds");
    }

    public Blockchain(bool quiet = true) {
        if (File.Exists(BlockchainFile)) {
            foreach (var line in File.ReadAllLines(BlockchainFile)) {
                var ss = line.Split(' ').Select(Convert.FromHexString).ToArray();
                UpdateBalances(LastBlock = new(this, LastBlock, ss[1], ss[0], ss[2]));
                if (!quiet) Console.WriteLine(LastBlock);
            }
        }
    }

    public void Mine(byte[] myPublicKey, List<Transaction> transactions) {
        // Here we would add transactions to the block including our reward. Transactions we include would be based
        // on the fees they pay. There's a limit to the number of transactions we can include. Each would include:
        //   sender-address, receiver-address, amount, fee, sender-signature
        // we would add our special reward transaction:
        //   receiver=our address, amount=reward+fees
        // amount is recorded as convenience and must be checked against current reward amount and sum of the fees.
        LastBlock = new Block(this, LastBlock, transactions, myPublicKey);
        UpdateBalances(transactions, myPublicKey, Transaction.MicroReward);
        File.AppendAllLines(BlockchainFile, [LastBlock.FileString()]);
    }
}

public class Block {
    public Block Previous { get; }
    public byte[] Nonce { get; }
    public byte[] Data { get; }
    public byte[] Hash { get; }
    public int HashCount { get; }

    public Block(Blockchain bc, Block previous, IList<Transaction> transactions, byte[] myPublicKey) :
        this(bc, previous, MakeData(bc, transactions, myPublicKey)) {
    }

    private static byte[] MakeData(Blockchain bc, IList<Transaction> transactions, byte[] myPublicKey) {
        ulong reward = transactions.Aggregate(Transaction.MicroReward, (sum, tx) => {
            checked {
                return sum + tx.MicroFee;
            }
        });
        bc.ValidateTransactions(transactions, reward);
        return [.. transactions.SelectMany(tx => tx.Buffer).ToArray(), .. myPublicKey, ..BitConverter.GetBytes(reward)];
    }

    public Block(Blockchain bc, Block previous, byte[] data, byte[] nonce = null, byte[] hash = null) {
        Previous = previous;
        Data = data;
        VerifyBlockData(bc);
        if (nonce == null) new Random().NextBytes(Nonce = new byte[32]);
        else Nonce = nonce;
        int nonceOffset = Previous?.Hash?.Length ?? 0;
        byte[] buf = [.. Previous?.Hash ?? [], .. Nonce, .. Data];
        if (hash == null) {
            do { // mine loop - increment nonce
                for (int i = 0; i < 32 && ++buf[i + nonceOffset] == 0; i++) ;
                Hash = SHA256.HashData(buf);
                HashCount++;
            } while (!bc.Difficulty.SequenceEqual(Hash.Take(bc.Difficulty.Length)));
            Buffer.BlockCopy(buf, nonceOffset, Nonce, 0, Nonce.Length);
        } else {
            Contract.Assert(bc.Difficulty.SequenceEqual(hash.Take(bc.Difficulty.Length)), "Invalid hash");
            Hash = SHA256.HashData(buf);
            Contract.Assert(Hash.SequenceEqual(hash), "Invalid hash");
        }
    }

    public string FileString() => string.Join(" ", new[] { Nonce, Data, Hash }.Select(Convert.ToHexString));

    public override string ToString() => $"nonce={Convert.ToHexString(Nonce)} hash={Convert.ToHexString(Hash)}";

    public (List<Transaction> transactions, byte[] rewardPublicKey, ulong rewardAmount) ReadData() {
        int txCount = Data.Length / Transaction.BinaryLength, remainder = Data.Length % Transaction.BinaryLength;
        Contract.Assert(remainder == 140 + 8, "expected reward transaction at end");
        List<Transaction> transactions = new(txCount);
        for (int i = 0, si = 0; i < txCount; i++)
            transactions.Add(new(Data.AsSpan()[si..(si += Transaction.BinaryLength)]));
        return (transactions, Data.AsSpan()[^148..^8].ToArray(), BitConverter.ToUInt64(Data.AsSpan()[^8..]));
    }

    private void VerifyBlockData(Blockchain bc) {
        (List<Transaction> transactions, _, ulong rewardAmount) = ReadData();
        bc.ValidateTransactions(transactions, rewardAmount);
    }
}

public class Transaction {
    public const ulong MicroReward = 1_000_000; // 1 coin
    public const int BinaryLength = 140 + 140 + 8 + 8 + 128; // 424 bytes
    public byte[] Sender { get; }
    public byte[] Receiver { get; }
    public ulong MicroAmount { get; }
    public ulong MicroFee { get; }
    public byte[] Signature { get; }

    public Transaction(byte[] sender, byte[] receiver, ulong microAmount, ulong microFee, byte[] privateKey) {
        Contract.Assert(sender.Length == 140 && receiver.Length == 140, "Invalid public key length");
        Contract.Assert(privateKey.Length >= 600, "Invalid private key length");
        Sender = sender;
        Receiver = receiver;
        MicroAmount = microAmount;
        MicroFee = microFee;
        byte[] buffer = [
            .. sender, .. receiver,
            .. BitConverter.GetBytes(microAmount), .. BitConverter.GetBytes(microFee)
        ];
        using (RSACryptoServiceProvider rsa = new()) {
            rsa.ImportRSAPrivateKey(privateKey, out _);
            Signature = rsa.SignData(buffer, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
    }

    public Transaction(ReadOnlySpan<byte> buffer) {
        Contract.Assert(buffer.Length == BinaryLength, "Invalid buffer length");
        Sender = buffer[..140].ToArray();
        Receiver = buffer[140..280].ToArray();
        MicroAmount = BitConverter.ToUInt64(buffer[280..288]);
        MicroFee = BitConverter.ToUInt64(buffer[288..296]);
        Signature = buffer[296..].ToArray();
        using (RSACryptoServiceProvider rsa = new()) {
            rsa.ImportRSAPublicKey(Sender, out _);
            Contract.Assert(
                rsa.VerifyData(buffer[..296], Signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                "Invalid signature");
        }
    }

    public byte[] Buffer => [
        .. Sender, .. Receiver, .. BitConverter.GetBytes(MicroAmount), .. BitConverter.GetBytes(MicroFee),
        .. Signature
    ];
}

public class Wallet : IDisposable {
    public const string walletFile = "wallet.dat";
    public readonly byte[] PublicKey, PrivateKey;

    public Wallet() {
        byte[] walletData;
        if (File.Exists(walletFile)) {
            walletData = File.ReadAllBytes(walletFile);
            PublicKey = walletData[..140];
            PrivateKey = walletData[140..];
        } else {
            using (RSACryptoServiceProvider rsa = new()) {
                PublicKey = rsa.ExportRSAPublicKey();
                PrivateKey = rsa.ExportRSAPrivateKey();
            }
            walletData = [.. PublicKey, .. PrivateKey];
            File.WriteAllBytes(walletFile, walletData);
        }
        Array.Clear(walletData); // clear wallet data from memory for security
    }

    public Transaction CreateTransaction(byte[] receiver, ulong microAmount, ulong microFee) =>
        new(PublicKey, receiver, microAmount, microFee, PrivateKey);

    public override string ToString() => $"{Convert.ToHexString(PublicKey)}";

    public void Dispose() => Array.Clear(PrivateKey); // clear private key from memory for security
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