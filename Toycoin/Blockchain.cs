using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;

namespace Toycoin;

public class Blockchain {
    private readonly string blockchainFile = "blockchain.txt";
    public Block LastBlock { get; private set; }

    public byte[] Difficulty { get; } = // must have 3 leading zeros to be less than this
        [0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    public Toycoin MicroReward { get; } = 1_000_000; // 1 toycoin mining reward
    public int MaxTransactions { get; } = 10; // maximum transactions per block

    // initialize to what File.GetLastWriteTimeUtc returns for a non-existent file
    private DateTime lastFileDateTime = DateTime.FromFileTimeUtc(0);

    private readonly Lock balancesLock = new();
    private readonly Dictionary<byte[], Toycoin> balances = new(ByteArrayComparer.Instance); // balances by public key
    private readonly HashSet<byte[]> signatures = new(ByteArrayComparer.Instance); // unique transaction signatures

    private void UpdateBalances(Block block, bool justCheck = false) =>
        UpdateBalances(block.ReadTransactions(), block.RewardPublicKey, block.TotalMicroRewardAmount, justCheck);

    public void ValidateTransactions(IEnumerable<Transaction> transactions, ReadOnlySpan<byte> rewardPublicKey,
        Toycoin totalMicroRewardAmount) =>
        UpdateBalances(transactions, rewardPublicKey, totalMicroRewardAmount, justCheck: true);

    private void UpdateBalances(IEnumerable<Transaction> transactions, ReadOnlySpan<byte> rewardPublicKey,
        Toycoin totalMicroRewardAmount, bool justCheck = false) {
        var checkReward = MicroReward;
        // group transactions by sender and receiver with adds and subs so we can confirm that each account has
        // enough funds to cover the transactions before we update the balances
        Dictionary<byte[], Toycoin> adds = new(ByteArrayComparer.Instance), subs = new(ByteArrayComparer.Instance);
        HashSet<byte[]> newSignatures = new(ByteArrayComparer.Instance);
        var addsLookup = adds.GetAlternateLookup<ReadOnlySpan<byte>>();
        var subsLookup = subs.GetAlternateLookup<ReadOnlySpan<byte>>();
        var signaturesLookup = signatures.GetAlternateLookup<ReadOnlySpan<byte>>();
        var newSignaturesLookup = newSignatures.GetAlternateLookup<ReadOnlySpan<byte>>();
        foreach (var tx in transactions) {
            CollectionsMarshal.GetValueRefOrAddDefault(addsLookup, tx.Receiver, out _) += tx.MicroAmount;
            Toycoin totalSubtract = tx.MicroAmount + tx.MicroFee;
            CollectionsMarshal.GetValueRefOrAddDefault(subsLookup, tx.Sender, out _) += totalSubtract;
            checkReward += tx.MicroFee;
            // since transactions include block id, we should never have a legitimate duplicate signature
            Contract.Assert(!signaturesLookup.Contains(tx.Signature), "Replayed transaction");
            // we should never have a duplicate signature in the same block, that'd be just silly
            Contract.Assert(newSignaturesLookup.Add(tx.Signature), "Duplicate transaction");
        }
        Contract.Assert(checkReward == totalMicroRewardAmount, "Invalid total reward amount");
        CollectionsMarshal.GetValueRefOrAddDefault(addsLookup, rewardPublicKey, out _) += totalMicroRewardAmount;

        // check and update balances
        lock (balancesLock) { // not strictly necessary here since we're not using threads, but good practice
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
    
    public Toycoin GetBalance(ReadOnlySpan<byte> publicKey) {
        var balancesLookup = balances.GetAlternateLookup<ReadOnlySpan<byte>>();
        return balancesLookup.TryGetValue(publicKey, out var balance) ? balance : 0ul;
    }
    
    public Blockchain(string blockchainFilename = null, Action<Block> onBlockLoad = null) {
        Contract.Assert(Difficulty.Length == Block.HashLength, "Invalid difficulty length");
        if (blockchainFilename != null) blockchainFile = blockchainFilename;
        LoadNewBlocks(onBlockLoad);
    }

    public bool CheckBlock(Block block) => block.Hash.SequenceCompareTo(Difficulty) < 0;

    public bool LoadNewBlocks(Action<Block> onBlockLoad) {
        int skip = (int)(LastBlock?.BlockId ?? 0);
        var newFileDateTime = File.GetLastWriteTimeUtc(blockchainFile);
        if (lastFileDateTime == newFileDateTime) return false;
        foreach (var line in File.ReadLines(blockchainFile).Skip(skip)) {
            var parts = line.Split(' ').Select(Convert.FromHexString).ToArray(); // nonce, data, hash
            Contract.Assert(parts.Length == 3, "Invalid block format");
            LastBlock = new(this, parts[1], parts[0], parts[2]); // this, data, nonce, hash
            UpdateBalances(LastBlock);
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