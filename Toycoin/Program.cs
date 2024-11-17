using System.Diagnostics;
using System.Diagnostics.Contracts;

namespace Toycoin;

public static class Program {
    public static int Main(string[] args) {
        Console.OutputEncoding = System.Text.Encoding.UTF8; // so we can display the spinner characters properly

        if (args.Length > 0 && args[0] is "-h" or "--help" or "/?") {
            Console.WriteLine(@"Usage..: Toycoin [-w {walletFilename}] [-b {blockchainFilename}] [-t {threadCount}]");
            Console.WriteLine($"Default: Toycoin -w wallet.dat -b blockchain.dat -t {Environment.ProcessorCount}");
            return 0;
        }

        // default command line arguments
        string walletFilename = null, blockchainFilename = null;
        int threadCount = Environment.ProcessorCount;

        // parse command line arguments
        for (int i = 0; i < args.Length; i++) {
            if (args[i] is "-w" or "--wallet") {
                Contract.Assert(++i < args.Length, "Missing wallet filename");
                walletFilename = args[i];
            } else if (args[i] is "-b" or "--blockchain") {
                Contract.Assert(++i < args.Length, "Missing blockchain filename");
                blockchainFilename = args[i];
            } else if (args[i] is "-t" or "--threads") {
                Contract.Assert(++i < args.Length, "Missing thread count");
                Contract.Assert(int.TryParse(args[i], out threadCount) && threadCount > 0,
                    $"Invalid thread count '{args[i]}'");
            } else {
                Contract.Assert(false, $"Invalid argument '{args[i]}'");
            }
        }

        Console.WriteLine("Loading...");
        var bc = new Blockchain(blockchainFilename, Console.WriteLine);
        Console.WriteLine("Mining...");

        using (Wallet wallet = new(walletFilename)) {
            var myPublicKey = wallet.PublicKey.ToArray();

            // hide cursor so spinner looks better, but handle Ctrl+C to show cursor before exiting
            Console.CancelKeyPress += (_, _) => {
                Console.Write("\e[?25h"); // show cursor
                Environment.Exit(-1);
            };
            try { // try/finally block to ensure cursor is shown
                Console.Write("\e[?25l"); // hide cursor

                List<Transaction> transactions = [];
                for (;;) { // mining loop
                    // Add some random transactions
                    transactions.AddRange(MakeRandomTransactions(bc, wallet, new Random()));
                    // get the highest paying transactions
                    var mineTxs = transactions.OrderByDescending(tx => tx.MicroFee).Take(bc.MaxTransactions).ToList();

                    // actual block mining
                    var startTime = Stopwatch.GetTimestamp();
                    int toBeMined = 1; // 1 if we need to continue mining, 0 if we found a block (or file changed)
                    int[] hashCounts = new int[threadCount]; // hash counts, 1/thread to avoid having to lock increment
                    Block minedBlock = null;
                    Parallel.For(0, threadCount, threadId => {
                        var block = new Block(bc, mineTxs, myPublicKey); // create a new block
                        bool done = false;
                        for (; toBeMined > 0 && !done; hashCounts[threadId]++) {
                            // only one thread checks for new blocks and updates the spinner, and only 1/256 times
                            if (threadId == 0 && block.Nonce[0] == 0) {
                                Spinner();
                                done = bc.CheckForNewBlocks();
                            }
                            done |= bc.CheckBlock(block.IncrementAndHash());
                        }
                        // if we are done AND we are the first thread to finish, capture this block (might not be valid)
                        if (done && Interlocked.CompareExchange(ref toBeMined, 0, 1) == 1) minedBlock = block;
                    });
                    // we either found a block or the file changed (or maybe both, but file change takes precedence)
                    if (!bc.LoadNewBlocks(onBlockLoad: Console.WriteLine)) {
                        if (bc.Commit(minedBlock)) {
                            transactions = transactions.Except(mineTxs).ToList(); // remove txs we just committed
                            int hashCount = hashCounts.Sum();
                            var elapsed = Stopwatch.GetElapsedTime(startTime).TotalSeconds;
                            Console.WriteLine($"{bc.LastBlock} {hashCount:N0} {elapsed:N3}s {
                                hashCount / elapsed / 1E6:N3}Mhps txs={transactions.Count:N0}");
                        } else {
                            Console.WriteLine("Block not committed");
                            bc.LoadNewBlocks(onBlockLoad: Console.WriteLine);
                        }
                    }
                }
            } finally {
                Console.Write("\e[?25h"); // show cursor before exit, even if we threw an exception
            }
        }
    }

    private static IEnumerable<Transaction> MakeRandomTransactions(Blockchain bc, Wallet wallet, Random random) =>
        Enumerable.Range(0, random.Next(bc.MaxTransactions * 3 / 2))
            .Select(_ => MakeRandomTransaction(bc, wallet, random));

    private static Transaction MakeRandomTransaction(Blockchain bc, Wallet wallet, Random random) {
        var receiver = new byte[Wallet.PublicKeyLength];
        random.NextBytes(receiver);
        // we want to make sure we don't overflow the reward, so we limit the amount to 2/3 of the average
        ulong reward = bc.MicroReward;
        Toycoin amount = (ulong)random.Next((int)(reward / (ulong)(bc.MaxTransactions * 3 / 2)));
        Toycoin fee = (ulong)random.Next(100);
        return new Transaction(bc.LastBlock?.BlockId ?? 0ul, receiver, amount, fee, wallet);
    }

    private static int previousSpinner = -1; // so we only write the spinner character when it changes

    private static void Spinner() {
        var t = DateTime.Now.Millisecond / 100; // spins around once per second
        if (t == previousSpinner) return; // only write if it changed
        Console.Write("⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏"[previousSpinner = t]);
        Console.Write('\b'); // backspace to so we can overwrite the character
    }
}