using System.Diagnostics;

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