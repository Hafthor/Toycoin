using System.Diagnostics.Contracts;

namespace Toycoin;

public class Blockchain {
    private readonly string blockchainFile = "blockchain.txt";
    public Block LastBlock { get; private set; }
    public byte[] Difficulty { get; } = [0, 0, 0]; // 3 leading zeros
    public ulong MicroReward { get; } = 1_000_000; // 1 toycoin
    public int MaxTransactions { get; } = 10;

    private DateTime lastFileDateTime = DateTime.FromFileTimeUtc(0);

    private readonly Dictionary<byte[], ulong> balances = new(ByteArrayComparer.Instance);
    private readonly HashSet<byte[]> signatures = new(ByteArrayComparer.Instance);

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
                Contract.Assert(!signatures.Contains(signature), "Replayed transaction");
                Contract.Assert(newSignatures.Add(signature), "Duplicate transaction");
            }
            Contract.Assert(checkReward == totalMicroRewardAmount, "Invalid total reward amount");
            adds[rewardPublicKeyArray] = adds.GetValueOrDefault(rewardPublicKeyArray, 0ul) + totalMicroRewardAmount;

            // check and update balances
            lock (balances) {
                foreach (var (key, value) in subs)
                    Contract.Assert(balances.GetValueOrDefault(key, 0ul) + adds.GetValueOrDefault(key, 0ul) >= value,
                        "Insufficient funds");
                if (justCheck) return;
                // perform adds first to avoid negative balances
                foreach (var (key, value) in adds)
                    balances[key] = balances.GetValueOrDefault(key, 0ul) + value;
                foreach (var (key, value) in subs)
                    balances[key] -= value; // balances MUST always have a value for key
                signatures.UnionWith(newSignatures);
            }
        }
    }

    public void ValidateTransactions(IEnumerable<Transaction> transactions, ReadOnlySpan<byte> rewardPublicKey, ulong totalMicroRewardAmount) =>
        UpdateBalances(transactions, rewardPublicKey, totalMicroRewardAmount, justCheck: true);

    public Blockchain(string blockchainFilename = null, Action<Block> onBlockLoad = null) {
        if (blockchainFilename != null) blockchainFile = blockchainFilename;
        LoadNewBlocks(onBlockLoad);
    }

    public bool CheckBlock(Block block) => block.Hash.IsLessThan(Difficulty);
    
    public bool LoadNewBlocks(Action<Block> onBlockLoad) {
        var newFileDateTime = File.GetLastWriteTimeUtc(blockchainFile);
        if (lastFileDateTime == newFileDateTime) return false;
        lastFileDateTime = newFileDateTime;
        int skip = (int)(LastBlock?.BlockId ?? 0);
        foreach (var line in File.ReadAllLines(blockchainFile).Skip(skip)) {
            var ss = line.Split(' ').Select(Convert.FromHexString).ToArray(); // nonce, data, hash
            Block block = new(this, LastBlock, ss[1], ss[0], ss[2]);
            UpdateBalances(LastBlock = block);
            onBlockLoad?.Invoke(LastBlock);
        }
        return true;
    }

    public bool CheckForNewBlocks() => lastFileDateTime != File.GetLastWriteTimeUtc(blockchainFile);

    public void Commit(Block block) {
        ulong expectedBlockId = 0;
        if (LastBlock!=null) expectedBlockId = LastBlock.BlockId + 1;
        Contract.Assert(block.BlockId == expectedBlockId, "Invalid block id");
        LastBlock = block;
        UpdateBalances(block.ReadTransactions(), block.RewardPublicKey, block.TotalMicroRewardAmount);
        string fileString = LastBlock.FileString();
        Contract.Assert(!CheckForNewBlocks(), "File has changed");
        File.AppendAllLines(blockchainFile, [fileString]);
        lastFileDateTime = File.GetLastWriteTimeUtc(blockchainFile);
    }
}