using System.Diagnostics;

namespace Toycoin;

public static class Program {
    public static void Main() {
        Console.OutputEncoding = System.Text.Encoding.UTF8; // so we can display the spinner properly
        byte[] myPublicKey;
        List<Transaction> transactions = [];
        using (Wallet wallet = new()) {
            myPublicKey = wallet.PublicKey.ToArray();
            var tx = wallet.CreateTransaction(myPublicKey, 0, 0); // create a dummy transaction
            Console.WriteLine($"tx: {tx}");
            transactions.Add(tx);
        }

        Console.WriteLine("Loading...");
        var bc = new Blockchain(Console.WriteLine);
        Console.WriteLine("Mining...");
        for (;;) { // mining loop
            // get the highest paying transactions
            var mineTxs = transactions.OrderByDescending(tx => tx.MicroFee).Take(bc.MaxTransactions).ToList();
            transactions = transactions.Except(mineTxs).ToList(); // remove the transactions we are mining for
            var startTime = Stopwatch.GetTimestamp();
            int hashCount = 0, toBeMined = 1;
            Parallel.For(0L, Environment.ProcessorCount, procId => {
                var block = new Block(bc, bc.LastBlock, mineTxs, myPublicKey);
                bool valid = false;
                if (procId == 0) {
                    for (; toBeMined > 0 && !valid; Interlocked.Increment(ref hashCount)) {
                        if (block.Nonce[0] == 0) {
                            Spinner();
                            if (bc.CheckForNewBlocks()) {
                                Interlocked.CompareExchange(ref toBeMined, 0, 1);
                            }
                        }
                        valid = bc.CheckBlock(block.IncrementAndHash());
                    }
                } else {
                    for (; toBeMined > 0 && !valid; Interlocked.Increment(ref hashCount)) {
                        valid = bc.CheckBlock(block.IncrementAndHash());
                    }
                }
                if (valid && Interlocked.CompareExchange(ref toBeMined, 0, 1) == 1) bc.Commit(block);
            });
            if (bc.CheckForNewBlocks()) {
                Console.WriteLine("File changed. Loading new blocks...");
                bc.LoadNewBlocks(Console.WriteLine);
            } else {
                var elapsed = Stopwatch.GetElapsedTime(startTime).TotalSeconds;
                Console.WriteLine($"{bc.LastBlock} {hashCount:N0} {elapsed:N3}s {hashCount / elapsed / 1E6:N3}Mhps");
            }
        }
    }
    
    private static int _previousSpinner = -1;

    private static void Spinner() {
        var t = DateTime.Now.Millisecond / 100;
        if (t == _previousSpinner) return;
        Console.Write("⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏"[_previousSpinner = t]);
        Console.Write('\b');
    }
}