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

            Console.WriteLine("Loading...");
            var bc = new Blockchain(blockchainFilename, Console.WriteLine);
            Console.WriteLine("Mining...");

            Console.CancelKeyPress += (_, _) => {
                Console.Write("\e[?25h"); // show cursor
                Environment.Exit(-1);
            };
            try {
                Console.Write("\e[?25l"); // hide cursor

                for (;;) { // mining loop
                    // Add random transactions
                    transactions.AddRange(MakeRandomTransactions(bc, wallet, new Random()));
                    // get the highest paying transactions
                    var mineTxs = transactions.OrderByDescending(tx => tx.MicroFee).Take(bc.MaxTransactions).ToList();
                    var startTime = Stopwatch.GetTimestamp();
                    int toBeMined = 1;
                    int[] hashCounts = new int[threadCount];
                    Parallel.For(0, threadCount, procId => {
                        var block = new Block(bc, bc.LastBlock, mineTxs, myPublicKey);
                        bool valid = false;
                        if (procId == 0) { // only only thread checks for new blocks and updates the spinner
                            for (; toBeMined > 0 && !valid; hashCounts[procId]++) {
                                if (block.Nonce[0] == 0) {
                                    Spinner();
                                    if (bc.CheckForNewBlocks()) {
                                        Interlocked.CompareExchange(ref toBeMined, 0, 1);
                                    }
                                }
                                valid = bc.CheckBlock(block.IncrementAndHash());
                            }
                        } else {
                            for (; toBeMined > 0 && !valid; hashCounts[procId]++) {
                                valid = bc.CheckBlock(block.IncrementAndHash());
                            }
                        }
                        if (valid && Interlocked.CompareExchange(ref toBeMined, 0, 1) == 1) {
                            bc.Commit(block);
                            transactions =
                                transactions.Except(mineTxs).ToList(); // remove transactions we just recorded
                        }
                    });
                    if (bc.CheckForNewBlocks()) {
                        Console.WriteLine("File changed. Loading new blocks...");
                        bc.LoadNewBlocks(onBlockLoad: Console.WriteLine);
                    } else {
                        var elapsed = Stopwatch.GetElapsedTime(startTime).TotalSeconds;
                        int hashCount = hashCounts.Sum();
                        Console.WriteLine(
                            $"{bc.LastBlock} {hashCount:N0} {elapsed:N3}s {hashCount / elapsed / 1E6:N3}Mhps txs={transactions.Count:N0}");
                    }
                }
            } finally {
                Console.Write("\e[?25h"); // show cursor
            }
        }
    }

    private static IEnumerable<Transaction> MakeRandomTransactions(Blockchain bc, Wallet wallet, Random random) =>
        Enumerable.Range(0, random.Next(bc.MaxTransactions * 3 / 2))
            .Select(_ => MakeRandomTransaction(bc, wallet, random));

    private static Transaction MakeRandomTransaction(Blockchain bc, Wallet wallet, Random random) {
        var receiver = new byte[140];
        random.NextBytes(receiver);
        ulong amount = (ulong)random.Next((int)(bc.MicroReward / (ulong)(bc.MaxTransactions * 3 / 2))),
            fee = (ulong)random.Next(100);
        return new Transaction(bc.LastBlock?.BlockId ?? 0ul, receiver, amount, fee, wallet);
    }

    private static int previousSpinner = -1;

    private static void Spinner() {
        var t = DateTime.Now.Millisecond / 100;
        if (t == previousSpinner) return;
        Console.Write("⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏"[previousSpinner = t]);
        Console.Write('\b');
    }
}