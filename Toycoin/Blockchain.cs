using System.Diagnostics.Contracts;

namespace Toycoin;

public class Blockchain {
    private const string BlockchainFile = "blockchain.txt";
    public Block LastBlock { get; private set; }
    public byte[] Difficulty { get; } = [0, 0, 0]; // 3 leading zeros
    public ulong MicroReward { get; } = 1_000_000; // 1 toycoin
    public int MaxTransactions { get; } = 10;

    private readonly bool _quiet;

    private DateTime _lastFileDateTime = DateTime.MinValue;
    private int _lastBlockCount = 0;

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

            // check and update balances
            lock (_balances) {
                foreach (var (key, value) in subs)
                    Contract.Assert(_balances.GetValueOrDefault(key, 0ul) + adds.GetValueOrDefault(key, 0ul) >= value,
                        "Insufficient funds");
                if (justCheck) return;
                // perform adds first to avoid negative balances
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
        if (!File.Exists(BlockchainFile)) LoadNewBlocks();
    }

    public bool CheckBlock(Block block) => block.Hash.IsLessThan(Difficulty);
    
    public void LoadNewBlocks() {
        _lastFileDateTime = File.GetLastWriteTimeUtc(BlockchainFile);
        foreach (var line in File.ReadAllLines(BlockchainFile).Skip(_lastBlockCount)) {
            _lastBlockCount++;
            var ss = line.Split(' ').Select(Convert.FromHexString).ToArray(); // nonce, data, hash
            UpdateBalances(LastBlock = new(this, LastBlock, ss[1], ss[0], ss[2]));
            if (!_quiet) Console.WriteLine(LastBlock);
        }
    }

    public bool CheckForNewBlocks() => _lastFileDateTime != File.GetLastWriteTimeUtc(BlockchainFile);

    public void Commit(Block block) {
        LastBlock = block;
        UpdateBalances(block.ReadTransactions(), block.RewardPublicKey, MicroReward);
        string fileString = LastBlock.FileString();
        Contract.Assert(!CheckForNewBlocks(), "File has changed");
        File.AppendAllLines(BlockchainFile, [fileString]);
        _lastFileDateTime = File.GetLastWriteTimeUtc(BlockchainFile);
        _lastBlockCount++;
    }
}