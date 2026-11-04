using System.Diagnostics;

namespace VariableOSC.SelfTest
{
    /// <summary>One synthetic launch of a made-up app: a global pointer chain leading to a cue name and a cue number on the heap.</summary>
    internal sealed class AppModel
    {
        public FakeProcess Proc = null!;
        public long ModuleBase;
        public long HeapBase;
        public long Root;
        public long Manager;
        public long CueState;
        public long NameBuffer;
        public long NumberAddress => CueState + 0x28;
    }

    internal static class Program
    {
        private static int failures;

        private static void Check(bool condition, string name, string? detail = null)
        {
            Console.WriteLine($"{(condition ? "PASS" : "FAIL")}  {name}{(detail != null ? "  - " + detail : "")}");

            if (!condition)
            {
                failures++;
            }
        }

        private static int Main(string[] args)
        {
            bool perf = args.Contains("--perf");

            TestCodec();
            TestMatcher();
            TestScanAndVerify(pointerSize: 8);
            TestScanAndVerify(pointerSize: 4);
            TestCommonValueTrap();
            TestConsensus();
            TestDiagnostics();
            TestCancellation();

            if (perf)
            {
                TestPerformance();
            }

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL TESTS PASSED" : $"{failures} TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------
        // Synthetic app
        // ------------------------------------------------------------

        private static AppModel BuildApp(int seed, string cueName, int cueNumber, bool decoyAimedAtCue, int heapMegabytes = 8, int pointerSize = 8)
        {
            Random rnd = new(seed);
            FakeProcess proc = new();

            proc.PointerSize = pointerSize;

            // 32-bit programs live below 4 GB; 64-bit ones far above it.
            long moduleBase = pointerSize == 4
                ? 0x0040_0000L + rnd.Next(0x100) * 0x10000L
                : 0x7FF6_0000_0000L + rnd.Next(0x1000) * 0x10000L;
            long sysBase = pointerSize == 4
                ? 0x7500_0000L + rnd.Next(0x100) * 0x10000L
                : 0x7FFA_0000_0000L + rnd.Next(0x1000) * 0x10000L;
            long heapBase = pointerSize == 4
                ? 0x0300_0000L + rnd.Next(0x100) * 0x10000L
                : 0x0000_0210_0000_0000L + rnd.Next(0x10000) * 0x10000L;
            int heapSize = heapMegabytes << 20;

            proc.ModuleList.Add(new ModuleRange { Name = "app.exe", Base = moduleBase, Size = 0x200000, IsMain = true });
            proc.ModuleList.Add(new ModuleRange { Name = "kernel32.dll", Base = sysBase, Size = 0x100000, IsSystem = true });

            FakeProcess.Block data = proc.AddBlock(moduleBase + 0x100000, 0x10000, true);
            FakeProcess.Block sysData = proc.AddBlock(sysBase + 0x80000, 0x4000, true);
            FakeProcess.Block heap = proc.AddBlock(heapBase, heapSize, true);

            proc.FillNoise(heap, rnd, heapBase, heapSize, 20);
            proc.FillNoise(data, rnd, heapBase, heapSize, 5);
            proc.FillNoise(sysData, rnd, heapBase, heapSize, 5);

            int slots = heapSize / 0x1000;
            HashSet<int> used = new();

            long Place()
            {
                int slot;

                do
                {
                    slot = rnd.Next(slots);
                }
                while (!used.Add(slot));

                return heapBase + slot * 0x1000L + rnd.Next(0, 0x80) * 16L;
            }

            long root = Place();
            long manager = Place();
            long cueState = Place();
            long nameBuffer = Place();

            // root.+20 -> manager        root.+40 -> cueState
            // manager.+58 -> cueState    cueState.+10 -> nameBuffer, cueState.+28 = number
            proc.WritePointer(root + 0x20, manager);
            proc.WritePointer(root + 0x40, cueState);
            proc.WritePointer(manager + 0x58, cueState);
            proc.WritePointer(cueState + 0x10, nameBuffer);
            proc.WriteInt(cueState + 0x28, cueNumber);
            proc.WriteAscii(nameBuffer, cueName);

            // The one stable global: app.exe+0x100040 -> root.
            proc.WritePointer(moduleBase + 0x100040, root);

            // A decoy: a global that happens to point near the cue state
            // right now (like a cached pointer) but is junk after a restart.
            proc.WritePointer(
                moduleBase + 0x100080,
                decoyAimedAtCue ? cueState + 8 : heapBase + (rnd.NextInt64(heapSize) & ~7L));

            return new AppModel
            {
                Proc = proc,
                ModuleBase = moduleBase,
                HeapBase = heapBase,
                Root = root,
                Manager = manager,
                CueState = cueState,
                NameBuffer = nameBuffer
            };
        }

        private static bool HasChain(IEnumerable<PointerPath> paths, long baseOffset, params long[] offsets)
        {
            return paths.Any(p =>
                p.ModuleName == "app.exe" &&
                p.BaseOffset == baseOffset &&
                p.Offsets.SequenceEqual(offsets));
        }

        private static List<PointerPath> Decode(PointerScanResult result)
        {
            // Round-trip through the codec, exactly as the app stores them.
            return PointerChainCodec.DecodeAll(result.Candidates.Select(PointerChainCodec.Encode));
        }

        // ------------------------------------------------------------
        // Tests
        // ------------------------------------------------------------

        private static void TestCodec()
        {
            PointerPath chain = new()
            {
                ModuleName = "Hog4PC.exe",
                BaseOffset = 0x1A2B3C,
                IsPointerChain = true,
                Offsets = new List<long> { 0x10, 0x0, 0x48 }
            };

            string text = PointerChainCodec.Encode(chain);
            bool ok = PointerChainCodec.TryDecode(text, out PointerPath back);

            Check(ok && back.ModuleName == chain.ModuleName && back.BaseOffset == chain.BaseOffset &&
                  back.IsPointerChain && back.Offsets.SequenceEqual(chain.Offsets),
                "codec round-trips a chain", text);

            PointerPath direct = new() { ModuleName = "a.exe", BaseOffset = 0x500, IsPointerChain = false };
            ok = PointerChainCodec.TryDecode(PointerChainCodec.Encode(direct), out back);
            Check(ok && !back.IsPointerChain && back.Offsets.Count == 0 && back.BaseOffset == 0x500,
                "codec round-trips a direct module+offset");

            Check(!PointerChainCodec.TryDecode("garbage", out _) &&
                  !PointerChainCodec.TryDecode("a|zz|1", out _) &&
                  !PointerChainCodec.TryDecode("", out _),
                "codec rejects malformed text");
        }

        private static void TestMatcher()
        {
            Check(ValueMatcher.Matches(" Cue 12 ", "cue 12"), "string match ignores case and padding");
            Check(!ValueMatcher.Matches("Cue 12", "Cue 13"), "string mismatch detected");
            Check(ValueMatcher.Matches(13, 13) && !ValueMatcher.Matches(14, 13), "integer match is exact");
            Check(ValueMatcher.Matches(3.14159f, 3.1416f), "float match uses a small tolerance");
            Check(!ValueMatcher.Matches(float.NaN, 1.0f), "NaN never matches");
            Check(!ValueMatcher.Matches(null, 5), "null never matches");
            Check(ValuePlausibility.IsPlausible("Cue 12") && !ValuePlausibility.IsPlausible("Cu\u0001e"), "plausibility rejects control characters");
        }

        private static void TestScanAndVerify(int pointerSize)
        {
            Console.WriteLine();
            Console.WriteLine($"-- {pointerSize * 8}-bit target: scan launch 1, restart, verify launch 2 --");

            AppModel launch1 = BuildApp(seed: 1, "Cue 12 - Intro", 12, decoyAimedAtCue: true, pointerSize: pointerSize);
            AppModel launch2 = BuildApp(seed: 2, "Cue 13 - Verse", 13, decoyAimedAtCue: false, pointerSize: pointerSize);

            Check(launch1.HeapBase != launch2.HeapBase && launch1.ModuleBase != launch2.ModuleBase,
                "the two launches have different addresses (simulated ASLR)");

            PointerScanOptions options = new() { ChunkBytes = 1 << 16 };
            List<long> targets = new() { launch1.NameBuffer, launch1.NumberAddress };

            Stopwatch clock = Stopwatch.StartNew();
            List<PointerScanResult> results = PointerScanner.Scan(launch1.Proc, targets, options);
            clock.Stop();

            PointerScanResult nameResult = results[0];
            PointerScanResult numberResult = results[1];
            List<PointerPath> nameCandidates = Decode(nameResult);
            List<PointerPath> numberCandidates = Decode(numberResult);

            Console.WriteLine($"   scan took {clock.ElapsedMilliseconds} ms; name candidates {nameCandidates.Count}, number candidates {numberCandidates.Count}");

            Check(HasChain(numberCandidates, 0x100040, 0x20, 0x58, 0x28), "scan finds the 3-pointer chain to the cue number");
            Check(HasChain(numberCandidates, 0x100040, 0x40, 0x28), "scan finds the 2-pointer chain to the cue number");
            Check(HasChain(numberCandidates, 0x100080, 0x20), "scan also finds the decoy chain to the cue number");
            Check(HasChain(nameCandidates, 0x100040, 0x40, 0x10, 0x0), "scan finds the 3-pointer chain to the cue name");
            Check(HasChain(nameCandidates, 0x100040, 0x20, 0x58, 0x10, 0x0), "scan finds the 4-pointer chain to the cue name");

            // Every candidate must actually resolve to its target in launch 1.
            bool allResolve = numberCandidates.All(p =>
                PointerResolver.TryResolve(launch1.Proc, p, out long address) && address == launch1.NumberAddress);
            Check(allResolve, "every number candidate resolves to the target in launch 1");

            bool namesResolve = nameCandidates.All(p =>
                PointerResolver.TryResolve(launch1.Proc, p, out long address) && address == launch1.NameBuffer);
            Check(namesResolve, "every name candidate resolves to the target in launch 1");

            // Restart check on launch 2: the user types what they see.
            VerificationResult numberCheck = PermanenceVerifier.Verify(
                launch2.Proc,
                numberCandidates,
                address => launch2.Proc.ReadInt(address),
                13);

            Console.WriteLine($"   number: checked {numberCheck.Checked}, resolved {numberCheck.Resolved}, matching {numberCheck.Matching}, survivors {numberCheck.Survivors.Count}");

            Check(numberCheck.ConsensusAddress == launch2.NumberAddress, "number verification lands on the real address in launch 2");
            Check(HasChain(numberCheck.Survivors, 0x100040, 0x20, 0x58, 0x28) &&
                  HasChain(numberCheck.Survivors, 0x100040, 0x40, 0x28),
                "both genuine number chains survive the restart");
            Check(!HasChain(numberCheck.Survivors, 0x100080, 0x20), "the decoy chain is eliminated by the restart");
            Check(numberCheck.Survivors.Count <= PermanenceWorkflowLimit, "survivor count is small enough to settle", numberCheck.Survivors.Count.ToString());

            VerificationResult nameCheck = PermanenceVerifier.Verify(
                launch2.Proc,
                nameCandidates,
                address => launch2.Proc.ReadAscii(address, 64),
                "Cue 13 - Verse");

            Console.WriteLine($"   name:   checked {nameCheck.Checked}, resolved {nameCheck.Resolved}, matching {nameCheck.Matching}, survivors {nameCheck.Survivors.Count}");

            Check(nameCheck.ConsensusAddress == launch2.NameBuffer, "name verification lands on the real address in launch 2");
            Check(HasChain(nameCheck.Survivors, 0x100040, 0x40, 0x10, 0x0) &&
                  HasChain(nameCheck.Survivors, 0x100040, 0x20, 0x58, 0x10, 0x0),
                "both genuine name chains survive the restart");
            Check(!HasChain(nameCheck.Survivors, 0x100080, 0x8, 0x0), "the name decoy is eliminated by the restart");

            // A target at an odd address (e.g. text inside a larger buffer) is still traceable.
            List<PointerScanResult> odd = PointerScanner.Scan(launch1.Proc, new[] { launch1.NameBuffer + 3 }, options);
            Check(HasChain(Decode(odd[0]), 0x100040, 0x40, 0x10, 0x3), "an unaligned target (buffer + 3) still gets its chain");

            // A wrong typed value must not "verify" anything.
            VerificationResult wrong = PermanenceVerifier.Verify(
                launch2.Proc,
                numberCandidates,
                address => launch2.Proc.ReadInt(address),
                99999);
            Check(wrong.Survivors.Count == 0 && wrong.AllMatching.Count == 0, "a wrong typed value leaves no survivors");
            Check(wrong.Readings.Count > 0, "a failed check reports what the chains actually read", string.Join(", ", wrong.Readings.Select(r => $"{r.Text}x{r.Count}")));

            // Once promoted, the surviving chains drive consensus in launch 2.
            ConsensusResult consensus = PointerResolver.ResolveConsensus(launch2.Proc, numberCheck.Survivors);
            Check(consensus.Health == PermanenceHealth.Good && consensus.Address == launch2.NumberAddress,
                "promoted chains read as Good and agree on the real address", $"{consensus.Agreeing}/{consensus.Total}");

            // The chains must also have found a *third* launch with no further learning.
            AppModel launch3 = BuildApp(seed: 3, "Cue 14 - Chorus", 14, decoyAimedAtCue: false, pointerSize: pointerSize);
            ConsensusResult third = PointerResolver.ResolveConsensus(launch3.Proc, numberCheck.Survivors);
            Check(third.Health == PermanenceHealth.Good && third.Address == launch3.NumberAddress,
                "the same chains find the value again in a third launch");
        }

        private const int PermanenceWorkflowLimit = 8;

        private static void TestConsensus()
        {
            Console.WriteLine();
            Console.WriteLine("-- consensus and health --");

            AppModel app = BuildApp(seed: 5, "Cue 1", 1, decoyAimedAtCue: true);

            PointerPath a = new() { ModuleName = "app.exe", BaseOffset = 0x100040, IsPointerChain = true, Offsets = new List<long> { 0x20, 0x58, 0x28 } };
            PointerPath b = new() { ModuleName = "app.exe", BaseOffset = 0x100040, IsPointerChain = true, Offsets = new List<long> { 0x40, 0x28 } };
            PointerPath decoyOnApp5 = new() { ModuleName = "app.exe", BaseOffset = 0x100080, IsPointerChain = true, Offsets = new List<long> { 0x20 } };
            PointerPath badBase = new() { ModuleName = "app.exe", BaseOffset = 0x1FFFF0, IsPointerChain = true, Offsets = new List<long> { 0x20 } };
            PointerPath noModule = new() { ModuleName = "missing.dll", BaseOffset = 0x10, IsPointerChain = true, Offsets = new List<long> { 0x0 } };

            ConsensusResult good = PointerResolver.ResolveConsensus(app.Proc, new[] { a, b });
            Check(good.Health == PermanenceHealth.Good && good.Agreeing == 2 && good.Address == app.NumberAddress, "two agreeing chains = Good");

            ConsensusResult goodWithDead = PointerResolver.ResolveConsensus(app.Proc, new[] { a, b, noModule });
            Check(goodWithDead.Health == PermanenceHealth.Good, "all resolving chains agree and at least half resolve = Good");

            ConsensusResult degraded = PointerResolver.ResolveConsensus(app.Proc, new[] { a, b, badBase });
            Console.WriteLine($"   badBase consensus: {degraded.Health} {degraded.Agreeing}/{degraded.Resolved}/{degraded.Total}");

            // Decoy still points at the cue in app 5 (aimed=true), so make a stale one by pointing at nothing useful.
            PointerPath stale = new() { ModuleName = "app.exe", BaseOffset = 0x100040, IsPointerChain = true, Offsets = new List<long> { 0x20, 0x18 } };
            ConsensusResult majority = PointerResolver.ResolveConsensus(app.Proc, new[] { a, b, stale });
            Check(majority.Health == PermanenceHealth.Degraded && majority.Agreeing == 2 && majority.Address == app.NumberAddress,
                "a strict majority with one dissenter = Degraded, majority wins");

            ConsensusResult split = PointerResolver.ResolveConsensus(app.Proc, new[] { a, stale });
            Check(split.Health == PermanenceHealth.Lost, "a 1-vs-1 split = Lost");

            ConsensusResult none = PointerResolver.ResolveConsensus(app.Proc, new[] { noModule });
            Check(none.Health == PermanenceHealth.Lost && none.Resolved == 0, "nothing resolving = Lost");

            ConsensusResult empty = PointerResolver.ResolveConsensus(app.Proc, Array.Empty<PointerPath>());
            Check(empty.Health == PermanenceHealth.NotApplicable, "no chains = NotApplicable");

            Check(decoyOnApp5.ModuleName.Length > 0, "(sanity) decoy path constructed");
        }

        /// <summary>
        /// The failure seen on a real cue number: a common value matches a
        /// WRONG group of chains that is bigger than the right group. The
        /// restart check must keep every matching group, and changing the
        /// value must then pick out the one that follows it.
        /// </summary>
        private static void TestCommonValueTrap()
        {
            Console.WriteLine();
            Console.WriteLine("-- common value: wrong group bigger than the right one --");

            const long moduleBase = 0x7FF6_0000_0000L;
            const long heapBase = 0x0000_0210_0000_0000L;

            FakeProcess proc = new();
            proc.ModuleList.Add(new ModuleRange { Name = "m.dll", Base = moduleBase, Size = 0x10000, IsMain = true });
            proc.AddBlock(moduleBase, 0x10000, true);
            proc.AddBlock(heapBase, 0x10000, true);

            long falseSpot = heapBase + 0x100;   // always 13, never changes
            long trueSpot = heapBase + 0x2000;   // the real cue number

            proc.WriteInt(falseSpot, 13);
            proc.WriteInt(trueSpot, 13);

            List<PointerPath> chains = new();

            for (int i = 0; i < 7; i++)
            {
                long slot = moduleBase + 0x100 + i * 8;
                proc.WritePointer(slot, i < 5 ? falseSpot : trueSpot);

                chains.Add(new PointerPath
                {
                    ModuleName = "m.dll",
                    BaseOffset = 0x100 + i * 8,
                    IsPointerChain = true,
                    Offsets = new List<long> { 0 }
                });
            }

            VerificationResult restart = PermanenceVerifier.Verify(proc, chains, a => proc.ReadInt(a), 13);
            Check(restart.AllMatching.Count == 7, "restart check keeps every matching chain, both groups", restart.AllMatching.Count.ToString());
            Check(restart.ConsensusAddress == falseSpot && restart.Survivors.Count == 5, "(the wrong group does win the vote at this stage)");

            // The real cue changes; the coincidence does not.
            proc.WriteInt(trueSpot, 47);

            VerificationResult confirm = PermanenceVerifier.Verify(proc, restart.AllMatching, a => proc.ReadInt(a), 47);
            Check(confirm.ConsensusAddress == trueSpot && confirm.Survivors.Count == 2,
                "confirming with a changed value picks the group that follows it", $"{confirm.Survivors.Count} chain(s)");

            VerificationResult failed = PermanenceVerifier.Verify(proc, chains.Take(5).ToList(), a => proc.ReadInt(a), 47);
            Check(failed.Survivors.Count == 0 && failed.Readings.Count > 0 && failed.Readings[0].Text == "13",
                "chains that only matched by coincidence fail the confirm and report what they read");
        }

        private static void TestDiagnostics()
        {
            Console.WriteLine();
            Console.WriteLine("-- empty-result diagnostics --");

            const long heapBase = 0x0000_0210_0000_0000L;
            const long moduleBase = 0x7FF6_0000_0000L;
            const long target = heapBase + 0x8000;

            FakeProcess Build(int linksPresent)
            {
                FakeProcess proc = new();
                proc.ModuleList.Add(new ModuleRange { Name = "app.exe", Base = moduleBase, Size = 0x10000, IsMain = true });
                proc.AddBlock(moduleBase, 0x10000, true);
                proc.AddBlock(heapBase, 0x10000, true);

                // slot1 -> target-0x10, slot2 -> slot1-8, slot3 -> slot2-8, slot4 -> slot3-8
                long previous = target - 0x10;

                for (int i = 1; i <= linksPresent; i++)
                {
                    long slot = heapBase + i * 0x1000L;
                    proc.WritePointer(slot, previous);
                    previous = slot - 8;
                }

                return proc;
            }

            PointerScanResult Scan(FakeProcess proc) =>
                PointerScanner.Scan(proc, new[] { target }, new PointerScanOptions())[0];

            PointerScanResult none = Scan(Build(0));
            string noneText = PointerScanDiagnostics.DescribeEmptyResult(none);
            Check(none.Candidates.Count == 0 && none.Hops[0].Links == 0 && noneText.Contains("Nothing in the program's memory points"),
                "diagnostic: nothing points at the address");

            PointerScanResult stops = Scan(Build(1));
            string stopsText = PointerScanDiagnostics.DescribeEmptyResult(stops);
            Check(stops.Hops[0].Links == 1 && stops.Hops[1].Links == 0 && stopsText.Contains("1 hop(s) and then stops"),
                "diagnostic: trail stops after one hop", stopsText);

            PointerScanResult deep = Scan(Build(4));
            string deepText = PointerScanDiagnostics.DescribeEmptyResult(deep);
            Check(deep.Candidates.Count == 0 && deep.Hops.All(h => h.Links == 1) && deepText.Contains("full 4 hops"),
                "diagnostic: trail runs the full depth without reaching a module", deepText);
        }

        private static void TestCancellation()
        {
            Console.WriteLine();
            AppModel app = BuildApp(seed: 6, "Cue", 1, decoyAimedAtCue: false);
            using CancellationTokenSource cts = new();
            cts.Cancel();

            bool threw = false;

            try
            {
                PointerScanner.Scan(app.Proc, new[] { app.NumberAddress }, new PointerScanOptions(), null, cts.Token);
            }
            catch (OperationCanceledException)
            {
                threw = true;
            }

            Check(threw, "a cancelled scan stops with OperationCanceledException");
        }

        private static void TestPerformance()
        {
            Console.WriteLine();
            Console.WriteLine("-- performance: 256 MB synthetic heap, 20% pointer-like words --");

            Stopwatch build = Stopwatch.StartNew();
            AppModel app = BuildApp(seed: 9, "Cue 12 - Intro", 12, decoyAimedAtCue: false, heapMegabytes: 256);
            Console.WriteLine($"   built in {build.ElapsedMilliseconds} ms");

            foreach ((string label, PointerScanOptions options) in new[]
            {
                ("standard", new PointerScanOptions()),
                ("deep", PermanenceWorkflowOptions.Deep())
            })
            {
                Stopwatch clock = Stopwatch.StartNew();

                List<PointerScanResult> results = PointerScanner.Scan(
                    app.Proc,
                    new[] { app.NameBuffer, app.NumberAddress },
                    options,
                    new Progress<PointerScanProgress>(p => { }));

                clock.Stop();

                List<PointerPath> number = Decode(results[1]);
                Console.WriteLine($"   {label}: {clock.Elapsed.TotalSeconds:F1} s, name {results[0].Candidates.Count} / number {results[1].Candidates.Count} candidates (truncated: {results[0].Truncated || results[1].Truncated})");
                Check(HasChain(number, 0x100040, 0x20, 0x58, 0x28), $"{label}: true chain present in a 256 MB heap");
            }
        }
    }

    /// <summary>Mirror of PermanenceWorkflow.OptionsFor(Deep) so the perf run doesn't need the Windows-only workflow file.</summary>
    internal static class PermanenceWorkflowOptions
    {
        public static PointerScanOptions Deep() => new()
        {
            MaxLevels = 5,
            MaxOffset = 0x800,
            MaxFrontier = 250_000,
            MaxNodesPerLevel = 1_000_000,
            MaxCandidatesPerTarget = 15_000
        };
    }
}
