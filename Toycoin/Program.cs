using System.Diagnostics;
using System.Diagnostics.Contracts;

namespace Toycoin;

public static class Program {
    public static int Main(string[] args) {
        Console.OutputEncoding = System.Text.Encoding.UTF8; // so we can display the spinner properly
        if (args.Length > 0 && args[0] is "-h" or "--help" or "/?") {
            Console.WriteLine(@"Usage..: Toycoin [-w {walletFilename}] [-b {blockchainFilename}] [-t {threadCount}]");
            Console.WriteLine($"Default: Toycoin -w wallet.dat -b blockchain.dat -t {Environment.ProcessorCount}");
            return 0;
        }
        
        string walletFilename = null, blockchainFilename = null;
        int threadCount = Environment.ProcessorCount;
        for (int i = 0; i < args.Length; i++) {
            if (args[i] == "-w") {
                walletFilename = args[++i];
            } else if (args[i] == "-b") {
                blockchainFilename = args[++i];
            } else if (args[i] == "-t") {
                threadCount = int.Parse(args[++i]);
                Contract.Assert(threadCount > 0, "Invalid thread count");
            } else {
                Contract.Assert(false, "Invalid argument " + args[i]);
            }
        }
        
        byte[] myPublicKey;
        List<Transaction> transactions = [];
        using (Wallet wallet = new(walletFilename)) {
            myPublicKey = wallet.PublicKey.ToArray();
            var tx = wallet.CreateTransaction(myPublicKey, 0, 0); // create a dummy transaction
            Console.WriteLine($"tx: {tx}");
            transactions.Add(tx);
        }

        Console.WriteLine("Loading...");
        var bc = new Blockchain(blockchainFilename, Console.WriteLine);
        Console.WriteLine("Mining...");
        
        Console.CancelKeyPress += (_, _) => {
            Console.Write("\e[?25h"); // show cursor
            Environment.Exit(-1);
        };
        Console.Write("\e[?25l"); // hide cursor
        
        for (;;) { // mining loop
            // get the highest paying transactions
            var mineTxs = transactions.OrderByDescending(tx => tx.MicroFee).Take(bc.MaxTransactions).ToList();
            transactions = transactions.Except(mineTxs).ToList(); // remove the transactions we are mining for
            var startTime = Stopwatch.GetTimestamp();
            int hashCount = 0, toBeMined = 1;
            Parallel.For(0, threadCount, procId => {
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
                bc.LoadNewBlocks(onBlockLoad: Console.WriteLine);
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