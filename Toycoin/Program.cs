using System.Diagnostics.Contracts;
using System.Security.Cryptography;

const ulong microReward = 1_000_000; // 1 coin
const string walletFile = "wallet.txt", blockchainFile = "blockchain.txt";
byte[] data;
using (Wallet wallet = new(File.Exists(walletFile) ? File.ReadAllText(walletFile) : null)) {
    Transaction tx = wallet.CreateTransaction(wallet.PublicKey, 0, 0), tx2 = new(data = tx.Buffer);
    Console.WriteLine("tx: " + Convert.ToHexString(data));
    data = data.Concat(wallet.PublicKey).Concat(BitConverter.GetBytes(microReward)).ToArray();
}

Block block = null;
Console.WriteLine("Loading...");
if (File.Exists(blockchainFile)) {
    foreach (var line in File.ReadAllLines(blockchainFile)) {
        Console.WriteLine(block = new(line, block));
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
    Console.WriteLine(block = new Block(data, block));
    File.AppendAllLines(blockchainFile, [block.FileString()]);
}

public class Block {
    private readonly Block _previous;
    private readonly byte[] _nonce, _data, _hash;

    public Block(byte[] data, Block previous) {
        _previous = previous;
        _nonce = new byte[32];
        _data = data;
        VerifyBlockData();
        _hash = new byte[32];
        Random rand = new();
        var buf = MakeBuffer();
        do { // mine loop - randomly change one byte in the nonce
            buf[rand.Next(_nonce.Length)] = (byte)rand.Next(256);
            Contract.Assert(SHA256.TryHashData(buf, _hash, out var hl) && hl == _hash.Length, "hash failed");
        } while (!HashValid(_hash));
        Buffer.BlockCopy(buf, 0, _nonce, 0, _nonce.Length);
    }

    public Block(string fileData, Block previous) {
        _previous = previous;
        var ss = fileData.Split(' ');
        Contract.Assert(ss.Length == 3, "Invalid file data");
        _nonce = Convert.FromHexString(ss[0]);
        _data = Convert.FromHexString(ss[1]);
        VerifyBlockData();
        _hash = Convert.FromHexString(ss[2]);
        Contract.Assert(CheckBlock(), "Invalid block data");
    }

    public string FileString() =>
        $"{Convert.ToHexString(_nonce)} {Convert.ToHexString(_data)} {Convert.ToHexString(_hash)}";

    public override string ToString() => $"nonce={Convert.ToHexString(_nonce)} hash={Convert.ToHexString(_hash)}";

    private byte[] MakeBuffer() => _nonce.Concat(_previous?._hash ?? []).Concat(_data).ToArray();

    private void VerifyBlockData() {
        // here we would check the transactions in the block to make sure each
        // transaction is signed correctly and the sender had sufficient funds
    }

    private bool CheckBlock() {
        var h = SHA256.HashData(MakeBuffer());
        return _nonce.Length == 32 && HashValid(h) && h.SequenceEqual(_hash);
    }

    private static bool HashValid(byte[] hash) => hash.Length == 32 && hash[0] == 0 && hash[1] == 0 && hash[2] == 0;
}

public class Transaction {
    private readonly byte[] _sender, _receiver;
    private readonly ulong _microAmount, _microFee;
    private readonly byte[] _signature;

    public Transaction(byte[] sender, byte[] receiver, ulong microAmount, ulong microFee, byte[] privateKey) {
        Contract.Assert(sender.Length == 140 && receiver.Length == 140, "Invalid public key length");
        Contract.Assert(privateKey.Length >= 600, "Invalid private key length");
        _sender = sender;
        _receiver = receiver;
        _microAmount = microAmount;
        _microFee = microFee;
        var buffer = sender.Concat(receiver).Concat(BitConverter.GetBytes(microAmount))
            .Concat(BitConverter.GetBytes(microFee)).ToArray();
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

    public byte[] Buffer => _sender.Concat(_receiver).Concat(BitConverter.GetBytes(_microAmount))
        .Concat(BitConverter.GetBytes(_microFee)).Concat(_signature).ToArray();
}

public class Wallet : IDisposable {
    public readonly byte[] PrivateKey, PublicKey;

    public Wallet(string line) {
        if (line == null) {
            using (RSACryptoServiceProvider rsa = new()) {
                PrivateKey = rsa.ExportRSAPrivateKey();
                PublicKey = rsa.ExportRSAPublicKey();
            }
        } else {
            string[] ss = line.Split(' ');
            Contract.Assert(ss.Length == 2, "Invalid wallet line");
            PrivateKey = Convert.FromHexString(ss[0]);
            PublicKey = Convert.FromHexString(ss[1]);
            Contract.Assert(PrivateKey.Length >= 600 && PublicKey.Length == 140, "Invalid key lengths");
        }
    }

    public Transaction CreateTransaction(byte[] receiver, ulong microAmount, ulong microFee) =>
        new(PublicKey, receiver, microAmount, microFee, PrivateKey);

    public void Dispose() {
        // clear private key from memory for security
        for (int i = 0; i < PrivateKey.Length; i++)
            PrivateKey[i] = 0;
    }
}