using System.Diagnostics.Contracts;

namespace Toycoin;

public class Blockchain {
    private readonly string blockchainFile = "blockchain.txt";
    public Block LastBlock { get; private set; }

    public byte[] Difficulty { get; } = // must have 3 leading zeros to be less than this
        [0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    public ulong MicroReward { get; } = 1_000_000; // 1 toycoin mining reward
    public int MaxTransactions { get; } = 10; // maximum transactions per block

    // initialize to what File.GetLastWriteTimeUtc returns for a non-existent file
    private DateTime lastFileDateTime = DateTime.FromFileTimeUtc(0);

    private readonly Dictionary<byte[], ulong> balances = new(ByteArrayComparer.Instance); // balances by public key
    private readonly Dictionary<byte[], ulong>.AlternateLookup<ReadOnlySpan<byte>> balancesLookup;
    private readonly HashSet<byte[]> signatures = new(ByteArrayComparer.Instance); // unique transaction signatures
    private readonly HashSet<byte[]>.AlternateLookup<ReadOnlySpan<byte>> signaturesLookup;

    private void UpdateBalances(Block block) =>
        UpdateBalances(block.ReadTransactions(), block.RewardPublicKey, block.TotalMicroRewardAmount);

    private void UpdateBalances(IEnumerable<Transaction> transactions, ReadOnlySpan<byte> rewardPublicKey,
        ulong totalMicroRewardAmount, bool justCheck = false) {
        checked { // check for overflow/underflow on all arithmetic operations
            var checkReward = MicroReward;
            // group transactions by sender and receiver with adds and subs so we can confirm that each account has
            // enough funds to cover the transactions before we update the balances
            Dictionary<byte[], ulong> adds = new(ByteArrayComparer.Instance), subs = new(ByteArrayComparer.Instance);
            HashSet<byte[]> newSignatures = new(ByteArrayComparer.Instance);
            Dictionary<byte[], ulong>.AlternateLookup<ReadOnlySpan<byte>>
                addsLookup = adds.GetAlternateLookup<ReadOnlySpan<byte>>(),
                subsLookup = subs.GetAlternateLookup<ReadOnlySpan<byte>>();
            HashSet<byte[]>.AlternateLookup<ReadOnlySpan<byte>>
                newSignaturesLookup = newSignatures.GetAlternateLookup<ReadOnlySpan<byte>>();
            foreach (var tx in transactions) {
                addsLookup[tx.Receiver] =
                    (addsLookup.TryGetValue(tx.Receiver, out ulong rxAdd) ? rxAdd : 0ul) + tx.MicroAmount;
                subsLookup[tx.Sender] =
                    (subsLookup.TryGetValue(tx.Sender, out ulong txSub) ? txSub : 0ul) + tx.MicroAmount + tx.MicroFee;
                checkReward += tx.MicroFee;
                // since transactions include block id, we should never have a legitimate duplicate signature
                Contract.Assert(!signaturesLookup.Contains(tx.Signature), "Replayed transaction");
                // we should never have a duplicate signature in the same block, that'd be just silly
                Contract.Assert(newSignaturesLookup.Add(tx.Signature), "Duplicate transaction");
            }
            Contract.Assert(checkReward == totalMicroRewardAmount, "Invalid total reward amount");
            addsLookup[rewardPublicKey] =
                (addsLookup.TryGetValue(rewardPublicKey, out ulong myAdd) ? myAdd : 0ul) + totalMicroRewardAmount;

            // check and update balances
            lock (balances) { // not strictly necessary here since we're not using threads, but good practice
                // check that each account has enough funds to cover the transactions
                foreach (var (key, value) in subs)
                    Contract.Assert(balances.GetValueOrDefault(key, 0ul) + adds.GetValueOrDefault(key, 0ul) >= value,
                        "Insufficient funds");
                if (justCheck) return;
                // update known signatures
                signatures.UnionWith(newSignatures);
                // update balances
                // perform adds first to avoid negative balances
                foreach (var (key, value) in adds)
                    balances[key] = balances.GetValueOrDefault(key, 0ul) + value;
                foreach (var (key, value) in subs)
                    balances[key] -= value; // balances MUST always have a value for key
            }
        }
    }

    public void ValidateTransactions(IEnumerable<Transaction> transactions, ReadOnlySpan<byte> rewardPublicKey,
        ulong totalMicroRewardAmount) =>
        UpdateBalances(transactions, rewardPublicKey, totalMicroRewardAmount, justCheck: true);

    public Blockchain(string blockchainFilename = null, Action<Block> onBlockLoad = null) {
        balancesLookup = balances.GetAlternateLookup<ReadOnlySpan<byte>>();
        signaturesLookup = signatures.GetAlternateLookup<ReadOnlySpan<byte>>();
        if (blockchainFilename != null) blockchainFile = blockchainFilename;
        LoadNewBlocks(onBlockLoad);
    }

    public bool CheckBlock(Block block) => block.Hash.SequenceCompareTo(Difficulty) < 0;

    public bool LoadNewBlocks(Action<Block> onBlockLoad) {
        int skip = (int)(LastBlock?.BlockId ?? 0);
        var newFileDateTime = File.GetLastWriteTimeUtc(blockchainFile);
        if (lastFileDateTime == newFileDateTime) return false;
        foreach (var line in File.ReadAllLines(blockchainFile).Skip(skip)) {
            var ss = line.Split(' ').Select(Convert.FromHexString).ToArray(); // nonce, data, hash
            Block block = new(this, LastBlock, ss[1], ss[0], ss[2]);
            UpdateBalances(LastBlock = block);
            onBlockLoad?.Invoke(LastBlock);
        }
        lastFileDateTime = newFileDateTime;
        return true;
    }

    public bool CheckForNewBlocks() => lastFileDateTime != File.GetLastWriteTimeUtc(blockchainFile);

    // returns false if the block was not committed
    public bool Commit(Block block) {
        ulong expectedBlockId = 0;
        if (LastBlock != null) expectedBlockId = LastBlock.BlockId + 1;
        Contract.Assert(block.BlockId == expectedBlockId, "Invalid block id");
        string fileString = block.FileString();
        if (CheckForNewBlocks()) return false; // last soft check before we update balances
        LastBlock = block;
        UpdateBalances(block.ReadTransactions(), block.RewardPublicKey, block.TotalMicroRewardAmount);
        Contract.Assert(!CheckForNewBlocks(), "File has changed after updating balances - cannot continue");
        File.AppendAllLines(blockchainFile, [fileString]);
        lastFileDateTime = File.GetLastWriteTimeUtc(blockchainFile);
        return true;
    }
}