using System.Diagnostics.Contracts;

namespace Toycoin;

public class Blockchain {
    private readonly string _blockchainFile = "blockchain.txt";
    public Block LastBlock { get; private set; }
    public byte[] Difficulty { get; } = [0, 0, 0]; // 3 leading zeros
    public ulong MicroReward { get; } = 1_000_000; // 1 toycoin
    public int MaxTransactions { get; } = 10;

    private DateTime _lastFileDateTime = DateTime.FromFileTimeUtc(0);
    private int _lastBlockCount = 0;

    private readonly Dictionary<byte[], ulong> _balances = new(ByteArrayComparer.Instance);
    private readonly HashSet<byte[]> _signatures = new(ByteArrayComparer.Instance);

    private void UpdateBalances(Block block) =>
        UpdateBalances(block.ReadTransactions(), block.RewardPublicKey, block.TotalMicroRewardAmount);

    private void UpdateBalances(IEnumerable<Transaction> transactions, ReadOnlySpan<byte> rewardPublicKey,
        ulong totalMicroRewardAmount, bool justCheck = false) {
        checked {
            var rewardPublicKeyArray = rewardPublicKey.ToArray();
            var checkReward = MicroReward;
            // group transactions by sender and receiver with adds and subs
            Dictionary<byte[], ulong> adds = new(ByteArrayComparer.Instance), subs = new(ByteArrayComparer.Instance);
            HashSet<byte[]> newSignatures = new(ByteArrayComparer.Instance);
            foreach (var tx in transactions) {
                byte[] sender = tx.Sender.ToArray(), receiver = tx.Receiver.ToArray();
                adds[receiver] = adds.GetValueOrDefault(receiver, 0ul) + tx.MicroAmount;
                subs[sender] = subs.GetValueOrDefault(sender, 0ul) + tx.MicroAmount + tx.MicroFee;
                checkReward += tx.MicroFee;
                byte[] signature = tx.Signature.ToArray();
                Contract.Assert(!_signatures.Contains(signature), "Replayed transaction");
                Contract.Assert(newSignatures.Add(signature), "Duplicate transaction");
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
                _signatures.UnionWith(newSignatures);
            }
        }
    }

    public void ValidateTransactions(IEnumerable<Transaction> transactions, ReadOnlySpan<byte> rewardPublicKey, ulong totalMicroRewardAmount) =>
        UpdateBalances(transactions, rewardPublicKey, totalMicroRewardAmount, justCheck: true);

    public Blockchain(string blockchainFilename = null, Action<Block> onBlockLoad = null) {
        if (blockchainFilename != null) _blockchainFile = blockchainFilename;
        LoadNewBlocks(onBlockLoad);
    }

    public bool CheckBlock(Block block) => block.Hash.IsLessThan(Difficulty);
    
    public bool LoadNewBlocks(Action<Block> onBlockLoad) {
        var newFileDateTime = File.GetLastWriteTimeUtc(_blockchainFile);
        if (_lastFileDateTime == newFileDateTime) return false;
        _lastFileDateTime = newFileDateTime;
        foreach (var line in File.ReadAllLines(_blockchainFile).Skip(_lastBlockCount)) {
            _lastBlockCount++;
            var ss = line.Split(' ').Select(Convert.FromHexString).ToArray(); // nonce, data, hash
            UpdateBalances(LastBlock = new(this, LastBlock, ss[1], ss[0], ss[2]));
            onBlockLoad?.Invoke(LastBlock);
        }
        return true;
    }

    public bool CheckForNewBlocks() => _lastFileDateTime != File.GetLastWriteTimeUtc(_blockchainFile);

    public void Commit(Block block) {
        LastBlock = block;
        UpdateBalances(block.ReadTransactions(), block.RewardPublicKey, block.TotalMicroRewardAmount);
        string fileString = LastBlock.FileString();
        Contract.Assert(!CheckForNewBlocks(), "File has changed");
        File.AppendAllLines(_blockchainFile, [fileString]);
        _lastFileDateTime = File.GetLastWriteTimeUtc(_blockchainFile);
        _lastBlockCount++;
    }
}