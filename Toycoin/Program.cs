using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Security.Cryptography;

const string walletFile = "wallet.dat", blockchainFile = "blockchain.txt";
byte[] data;
bool walletExists = File.Exists(walletFile);
byte[] walletData = walletExists ? File.ReadAllBytes(walletFile) : null;
byte[] walletPublicKey;
Transaction tx;
using (Wallet wallet = walletExists ? new(walletData) : new()) {
    if (!walletExists) {
        walletData = wallet.FileData;
        File.WriteAllBytes(walletFile, walletData);
    }
    Array.Clear(walletData);
    tx = wallet.CreateTransaction(walletPublicKey = wallet.PublicKey, 0, 0);
    Transaction tx2 = new(data = tx.Buffer);
    Console.WriteLine("tx: " + Convert.ToHexString(data));
    data = [.. data, .. wallet.PublicKey, ..  BitConverter.GetBytes(Transaction.MicroReward)];
}

Block block = null;
Console.WriteLine("Loading...");
if (File.Exists(blockchainFile)) {
    foreach (var line in File.ReadAllLines(blockchainFile)) {
        var ss = line.Split(' ').Select(Convert.FromHexString).ToArray();
        Console.WriteLine(block = new(block, ss[1], ss[0], ss[2]));
    }
}

Console.WriteLine("Mining...");
for (;;) { // mining loop
    // Here we would add transactions to the block including our reward. Transactions we include would be based
    // on the fees they pay. There's a limit to the number of transactions we can include. Each would include:
    //   sender-address, receiver-address, amount, fee, sender-signature
    // we would add our special reward transaction:
    //   receiver=our address, amount=reward+fees
    // amount is recorded as convenience and must be checked against current reward amount and sum of the fees.
    var sw = Stopwatch.GetTimestamp();
    block = new Block(block, tx == null ? [] : [tx], walletPublicKey);
    tx = null;
    var elapsed = Stopwatch.GetElapsedTime(sw).TotalSeconds;
    Console.WriteLine("{0} {1:N0} {2:N3}s {3}Mhps", block, block.HashCount, elapsed, block.HashCount / elapsed / 1E6);
    File.AppendAllLines(blockchainFile, [block.FileString()]);
}

public class Block {
    private readonly Block _previous;
    private readonly byte[] _nonce, _data, _hash;
    public readonly int HashCount;

    public Block(Block previous, IList<Transaction> transactions, byte[] myPublicKey) : this(previous,
        MakeData(transactions, myPublicKey)) {
    }
    
    private static byte[] MakeData(IList<Transaction> transactions, byte[] myPublicKey) {
        ulong reward = transactions.Aggregate(Transaction.MicroReward, (sum, tx) => sum + tx.MicroFee);
        return [.. transactions.SelectMany(tx => tx.Buffer).ToArray(), .. myPublicKey, ..BitConverter.GetBytes(reward)];
    }

    public Block(Block previous, byte[] data, byte[] nonce = null, byte[] hash = null) {
        _previous = previous;
        _data = data;
        VerifyBlockData();
        if (nonce == null) new Random().NextBytes(_nonce = new byte[32]);
        else _nonce = nonce;
        int nonceOffset = _previous?._hash?.Length ?? 0;
        byte[] buf = [.. _previous?._hash ?? [], .. _nonce, .. _data];
        if (hash == null) {
            do { // mine loop - increment nonce
                for (int i = 0; i < 32 && ++buf[i + nonceOffset] == 0; i++) ;
                _hash = SHA256.HashData(buf);
                HashCount++;
            } while (_hash[0] != 0 || _hash[1] != 0 || _hash[2] != 0);
            Buffer.BlockCopy(buf, nonceOffset, _nonce, 0, _nonce.Length);
        } else {
            _hash = SHA256.HashData(buf);
            Contract.Assert(_hash[0] == 0 && _hash[1] == 0 && _hash[2] == 0 && _hash.SequenceEqual(hash), "Invalid hash");
        }
    }

    public string FileString() => string.Join(" ", new[] { _nonce, _data, _hash }.Select(Convert.ToHexString));

    public override string ToString() => $"nonce={Convert.ToHexString(_nonce)} hash={Convert.ToHexString(_hash)}";

    private void VerifyBlockData() {
        // here we would check the transactions in the block to make sure each
        // transaction is signed correctly and the sender had sufficient funds
    }
}

public class Transaction {
    public const ulong MicroReward = 1_000_000; // 1 coin
    private readonly byte[] _sender, _receiver;
    private readonly ulong _microAmount, _microFee;
    private readonly byte[] _signature;
    
    public ulong MicroAmount => _microAmount;
    public ulong MicroFee => _microFee;

    public Transaction(byte[] sender, byte[] receiver, ulong microAmount, ulong microFee, byte[] privateKey) {
        Contract.Assert(sender.Length == 140 && receiver.Length == 140, "Invalid public key length");
        Contract.Assert(privateKey.Length >= 600, "Invalid private key length");
        _sender = sender;
        _receiver = receiver;
        _microAmount = microAmount;
        _microFee = microFee;
        byte[] buffer = [.. sender, .. receiver, 
            .. BitConverter.GetBytes(microAmount), .. BitConverter.GetBytes(microFee)];
        using (RSACryptoServiceProvider rsa = new()) {
            rsa.ImportRSAPrivateKey(privateKey, out _);
            _signature = rsa.SignData(buffer, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
    }

    public Transaction(byte[] buffer) {
        Contract.Assert(buffer.Length == 140 + 140 + 8 + 8 + 128, "Invalid buffer length");
        _sender = buffer[..140];
        _receiver = buffer[140..280];
        _microAmount = BitConverter.ToUInt64(buffer.AsSpan()[280..288]);
        _microFee = BitConverter.ToUInt64(buffer.AsSpan()[288..296]);
        _signature = buffer[296..];
        using (RSACryptoServiceProvider rsa = new()) {
            rsa.ImportRSAPublicKey(_sender, out _);
            Contract.Assert(
                rsa.VerifyData(buffer[..296], _signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                "Invalid signature");
        }
    }

    public byte[] Buffer => [
        .. _sender, .. _receiver, .. BitConverter.GetBytes(_microAmount), .. BitConverter.GetBytes(_microFee),
        .. _signature
    ];
}

public class Wallet : IDisposable {
    public readonly byte[] PublicKey, PrivateKey;

    public Wallet() {
        using (RSACryptoServiceProvider rsa = new()) {
            PublicKey = rsa.ExportRSAPublicKey();
            PrivateKey = rsa.ExportRSAPrivateKey();
        }
    }

    public Wallet(byte[] fileData) {
        PublicKey = fileData[..140];
        PrivateKey = fileData[140..];
    }

    public byte[] FileData => [..PublicKey, ..PrivateKey];

    public Transaction CreateTransaction(byte[] receiver, ulong microAmount, ulong microFee) =>
        new(PublicKey, receiver, microAmount, microFee, PrivateKey);

    public override string ToString() => $"{Convert.ToHexString(PublicKey)} {Convert.ToHexString(PrivateKey)}";

    public void Dispose() => Array.Clear(PrivateKey); // clear private key from memory for security
}