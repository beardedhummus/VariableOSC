using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace VariableOSC
{
    /// <summary>
    /// Brute-force pointer scanner: finds every chain of pointers, starting
    /// from a static address inside a loaded module, that leads to a target
    /// address right now. Most such chains are coincidence; the Make
    /// Permanent wizard filters them down by re-checking after a restart.
    ///
    /// Works backwards from the target. Pass 1 reads the writable memory
    /// once and finds every 8-byte-aligned slot whose value lands within
    /// MaxOffset bytes below the target (slot value + offset == target).
    /// Each such slot is a "node". Pass 2 repeats the search with the nodes
    /// as the new targets, and so on for MaxLevels passes. A node inside a
    /// module is a finished chain (module + offset is the same on every
    /// launch); a node on the heap is expanded further. One pass over
    /// memory serves every variable being scanned at once.
    /// </summary>
    public static class PointerScanner
    {
        // Bit-filter geometry: 2^24 bits = 2 MB. A memory word is only
        // looked up in the sorted frontier if its 1 KB bucket might hold a
        // frontier address, which rejects the vast majority of words with a
        // single bit test.
        private const int BucketShift = 10;
        private const int FilterBits = 1 << 24;
        private const int PageSize = 4096;
        private const int MaxStaticPerPass = 2_000_000;

        private sealed class Level
        {
            public long[] Address = Array.Empty<long>();
            public int[] Parent = Array.Empty<int>();
            public int[] Offset = Array.Empty<int>();
            public int[] Target = Array.Empty<int>();
            public int[] Cost = Array.Empty<int>();
            public int Count;
        }

        private struct RawNode
        {
            public long Address;
            public int Parent;
            public int Offset;
            public int Target;
            public int Cost;
        }

        /// <summary>
        /// Everything one pass accumulates. Heap nodes are bounded: when
        /// the cap is reached the costliest half is dropped and a cost
        /// ceiling keeps them out, so what survives is the best nodes, not
        /// merely the first ones met in address order. Static hits (the
        /// finished chains) are never dropped by that cap.
        /// </summary>
        private sealed class Collector
        {
            private readonly int capacity;
            private Dictionary<(long, int), int> heapIndex = new();
            private readonly Dictionary<(long, int), int> staticIndex = new();

            public readonly List<RawNode> Heap = new();
            public readonly List<(RawNode Node, ModuleRange Module)> Static = new();
            public long CostCeiling = long.MaxValue;
            public bool Capped;

            public Collector(int capacity)
            {
                this.capacity = Math.Max(1000, capacity);
            }

            public void AddHeap(RawNode node)
            {
                if (node.Cost > CostCeiling)
                {
                    Capped = true;
                    return;
                }

                (long, int) key = (node.Address, node.Target);

                if (heapIndex.TryGetValue(key, out int existing))
                {
                    // Same slot reached through a different suffix: keep the
                    // cheaper one. Both reach the target equally well today,
                    // and the cheaper is more likely a real struct field.
                    if (node.Cost < Heap[existing].Cost)
                    {
                        Heap[existing] = node;
                    }

                    return;
                }

                heapIndex[key] = Heap.Count;
                Heap.Add(node);

                if (Heap.Count >= capacity)
                {
                    Compact();
                }
            }

            public void AddStatic(RawNode node, ModuleRange module)
            {
                (long, int) key = (node.Address, node.Target);

                if (staticIndex.TryGetValue(key, out int existing))
                {
                    if (node.Cost < Static[existing].Node.Cost)
                    {
                        Static[existing] = (node, module);
                    }

                    return;
                }

                if (Static.Count >= MaxStaticPerPass)
                {
                    Capped = true;
                    return;
                }

                staticIndex[key] = Static.Count;
                Static.Add((node, module));
            }

            private void Compact()
            {
                Heap.Sort((a, b) => a.Cost.CompareTo(b.Cost));
                Heap.RemoveRange(Heap.Count / 2, Heap.Count - Heap.Count / 2);

                CostCeiling = Heap[Heap.Count - 1].Cost;
                Capped = true;

                heapIndex = new Dictionary<(long, int), int>(Heap.Count);

                for (int i = 0; i < Heap.Count; i++)
                {
                    heapIndex[(Heap[i].Address, Heap[i].Target)] = i;
                }
            }
        }

        /// <summary>One pass's lookup structures plus the matching step.</summary>
        private sealed class Pass
        {
            private readonly Level previous;
            private readonly long[] sortedAddresses;
            private readonly int[] order;
            private readonly ulong[] filter;
            private readonly int maxOffset;
            private readonly int alignment;
            private readonly List<ModuleRange> modules;
            private readonly Collector collector;
            private readonly int pointerSize;

            public readonly long MinFrontier;
            public readonly long MaxFrontier;

            public Pass(Level previous, int maxOffset, int alignment, int pointerSize, List<ModuleRange> modules, Collector collector)
            {
                this.pointerSize = pointerSize;
                this.previous = previous;
                this.maxOffset = maxOffset;
                this.alignment = alignment;
                this.modules = modules;
                this.collector = collector;

                order = Enumerable.Range(0, previous.Count).ToArray();
                Array.Sort(order, (a, b) => previous.Address[a].CompareTo(previous.Address[b]));

                sortedAddresses = new long[previous.Count];

                for (int i = 0; i < order.Length; i++)
                {
                    sortedAddresses[i] = previous.Address[order[i]];
                }

                filter = BuildFilter(sortedAddresses, maxOffset);

                MinFrontier = sortedAddresses[0] - maxOffset;
                MaxFrontier = sortedAddresses[sortedAddresses.Length - 1];
            }

            public void ScanBuffer(byte[] buffer, int length, long baseAddress, ModuleRange? chunkModule, bool mixed)
            {
                if (pointerSize == 4)
                {
                    ReadOnlySpan<uint> words32 = MemoryMarshal.Cast<byte, uint>(buffer.AsSpan(0, length & ~3));

                    for (int i = 0; i < words32.Length; i++)
                    {
                        long value = words32[i];

                        if (value < MinFrontier || value > MaxFrontier)
                        {
                            continue;
                        }

                        if (!PassesFilter(value))
                        {
                            continue;
                        }

                        Consider(baseAddress + (long)i * 4, value, chunkModule, mixed);
                    }

                    return;
                }

                ReadOnlySpan<long> words = MemoryMarshal.Cast<byte, long>(buffer.AsSpan(0, length & ~7));

                for (int i = 0; i < words.Length; i++)
                {
                    long value = words[i];

                    if (value < MinFrontier || value > MaxFrontier)
                    {
                        continue;
                    }

                    if (!PassesFilter(value))
                    {
                        continue;
                    }

                    Consider(baseAddress + (long)i * 8, value, chunkModule, mixed);
                }
            }

            private bool PassesFilter(long value)
            {
                int bit = (int)((value >> BucketShift) & (FilterBits - 1));
                return (filter[bit >> 6] & (1UL << (bit & 63))) != 0;
            }

            private void Consider(long slot, long value, ModuleRange? chunkModule, bool mixed)
            {
                int index = LowerBound(sortedAddresses, value);

                // The slot's module doesn't depend on which frontier node
                // matched, so decide it once.
                ModuleRange? module = chunkModule ?? (mixed ? FindModule(modules, slot) : null);

                while (index < sortedAddresses.Length)
                {
                    long delta = sortedAddresses[index] - value;

                    if (delta > maxOffset)
                    {
                        break;
                    }

                    if (delta % alignment == 0)
                    {
                        int parent = order[index];
                        int target = previous.Target[parent];

                        RawNode node = new()
                        {
                            Address = slot,
                            Parent = parent,
                            Offset = (int)delta,
                            Target = target,
                            Cost = previous.Cost[parent] + (int)delta + 16
                        };

                        if (module != null)
                        {
                            collector.AddStatic(node, module);
                        }
                        else
                        {
                            collector.AddHeap(node);
                        }
                    }

                    index++;
                }
            }
        }

        public static List<PointerScanResult> Scan(
            IPointerScanMemory memory,
            IReadOnlyList<long> targets,
            PointerScanOptions options,
            IProgress<PointerScanProgress>? progress = null,
            CancellationToken cancellation = default)
        {
            int maxLevels = Math.Max(1, options.MaxLevels);
            int pointerSize = memory.PointerSize == 4 ? 4 : 8;
            int alignment = Math.Max(1, options.Alignment);
            int maxOffset = Math.Max(alignment, options.MaxOffset);

            List<PointerScanResult> results = new();

            foreach (long target in targets)
            {
                results.Add(new PointerScanResult { TargetAddress = target, PointerSize = pointerSize });
            }

            if (targets.Count == 0)
            {
                return results;
            }

            List<Level> levels = new()
            {
                new Level
                {
                    Address = targets.ToArray(),
                    Parent = new int[targets.Count],
                    Offset = new int[targets.Count],
                    Target = Enumerable.Range(0, targets.Count).ToArray(),
                    Cost = new int[targets.Count],
                    Count = targets.Count
                }
            };

            List<ModuleRange> modules = memory.Modules.OrderBy(m => m.Base).ToList();
            IReadOnlyList<ScanRegion> regions = memory.Regions;
            long regionsTotal = regions.Sum(r => r.Size);
            long staticFound = 0;
            long nodesFound = 0;
            int perTargetFrontier = Math.Max(1, options.MaxFrontier / targets.Count);
            byte[] buffer = new byte[Math.Max(PageSize, options.ChunkBytes)];

            for (int depth = 1; depth <= maxLevels; depth++)
            {
                cancellation.ThrowIfCancellationRequested();

                Level previous = levels[depth - 1];

                if (previous.Count == 0)
                {
                    break;
                }

                // The first hop is measured to the variable itself, which
                // (a string inside a larger buffer, say) may sit at an odd
                // address; every later hop is measured to a pointer slot,
                // which is always aligned.
                int passAlignment = depth == 1 && targets.Any(t => t % alignment != 0) ? 1 : alignment;

                Collector collector = new(options.MaxNodesPerLevel);
                Pass pass = new(previous, maxOffset, passAlignment, pointerSize, modules, collector);

                long regionsDone = 0;
                long lastReport = Environment.TickCount64;

                foreach (ScanRegion region in regions)
                {
                    long cursor = region.Base;
                    long end = region.Base + region.Size;

                    while (cursor < end)
                    {
                        cancellation.ThrowIfCancellationRequested();

                        int want = (int)Math.Min(buffer.Length, end - cursor) & ~7;

                        if (want <= 0)
                        {
                            break;
                        }

                        ClassifyChunk(modules, cursor, cursor + want, out ModuleRange? chunkModule, out bool mixed);

                        if (memory.TryRead(cursor, buffer, want))
                        {
                            pass.ScanBuffer(buffer, want, cursor, chunkModule, mixed);
                        }
                        else
                        {
                            // Part of the chunk is unreadable (guard page,
                            // freed mid-scan). Fall back page by page so one
                            // bad page doesn't cost the whole chunk.
                            for (int pageStart = 0; pageStart < want; pageStart += PageSize)
                            {
                                int pageLength = Math.Min(PageSize, want - pageStart);

                                if (memory.TryRead(cursor + pageStart, buffer, pageLength))
                                {
                                    pass.ScanBuffer(buffer, pageLength, cursor + pageStart, chunkModule, mixed);
                                }
                            }
                        }

                        cursor += want;
                        regionsDone += want;

                        long now = Environment.TickCount64;

                        if (progress != null && now - lastReport > 150)
                        {
                            lastReport = now;

                            progress.Report(new PointerScanProgress(
                                depth,
                                maxLevels,
                                regionsTotal == 0 ? 1 : Math.Min(1.0, (double)regionsDone / regionsTotal),
                                nodesFound + collector.Heap.Count,
                                staticFound + collector.Static.Count));
                        }
                    }
                }

                foreach ((RawNode node, ModuleRange module) in collector.Static)
                {
                    AddCandidate(results[node.Target], levels, depth, node, module, options);
                }

                int[] linksPerTarget = new int[targets.Count];
                int[] chainsPerTarget = new int[targets.Count];

                foreach (RawNode node in collector.Heap)
                {
                    linksPerTarget[node.Target]++;
                }

                foreach ((RawNode node, ModuleRange _) in collector.Static)
                {
                    chainsPerTarget[node.Target]++;
                }

                for (int t = 0; t < results.Count; t++)
                {
                    results[t].Hops.Add(new ScanHopStat(depth, linksPerTarget[t], chainsPerTarget[t]));
                }

                staticFound += collector.Static.Count;
                nodesFound += collector.Heap.Count;

                if (collector.Capped)
                {
                    foreach (PointerScanResult result in results)
                    {
                        result.Truncated = true;
                    }
                }

                levels.Add(BuildNextLevel(collector.Heap, perTargetFrontier));

                progress?.Report(new PointerScanProgress(depth, maxLevels, 1.0, nodesFound, staticFound));
            }

            foreach (PointerScanResult result in results)
            {
                // The trail may have ended before the last hop; report the
                // hops that never ran as zeros so the explanation is complete.
                for (int hop = result.Hops.Count + 1; hop <= maxLevels; hop++)
                {
                    result.Hops.Add(new ScanHopStat(hop, 0, 0));
                }

                result.Candidates.Sort((a, b) => a.Score.CompareTo(b.Score));

                if (result.Candidates.Count > options.MaxCandidatesPerTarget)
                {
                    result.Candidates.RemoveRange(
                        options.MaxCandidatesPerTarget,
                        result.Candidates.Count - options.MaxCandidatesPerTarget);

                    result.Truncated = true;
                }
            }

            return results;
        }

        /// <summary>
        /// Most chunks lie wholly inside one module's image or wholly
        /// outside every module. Deciding that once per chunk avoids a
        /// module lookup per matching word; a chunk straddling a module
        /// edge (rare) falls back to a per-word lookup.
        /// </summary>
        private static void ClassifyChunk(
            List<ModuleRange> sortedModules,
            long start,
            long end,
            out ModuleRange? chunkModule,
            out bool mixed)
        {
            chunkModule = null;
            mixed = false;

            foreach (ModuleRange module in sortedModules)
            {
                long moduleEnd = module.Base + module.Size;

                if (module.Base >= end)
                {
                    break;
                }

                if (moduleEnd <= start)
                {
                    continue;
                }

                if (module.Base <= start && moduleEnd >= end)
                {
                    chunkModule = module;
                    return;
                }

                mixed = true;
                return;
            }
        }

        private static ulong[] BuildFilter(long[] sortedAddresses, int maxOffset)
        {
            ulong[] filter = new ulong[FilterBits / 64];

            foreach (long address in sortedAddresses)
            {
                long low = (address - maxOffset) >> BucketShift;
                long high = address >> BucketShift;

                for (long bucket = low; bucket <= high; bucket++)
                {
                    int bit = (int)(bucket & (FilterBits - 1));
                    filter[bit >> 6] |= 1UL << (bit & 63);
                }
            }

            return filter;
        }

        private static int LowerBound(long[] sorted, long value)
        {
            int low = 0;
            int high = sorted.Length;

            while (low < high)
            {
                int mid = (low + high) >> 1;

                if (sorted[mid] < value)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            return low;
        }

        private static ModuleRange? FindModule(List<ModuleRange> sortedModules, long address)
        {
            foreach (ModuleRange module in sortedModules)
            {
                if (address < module.Base)
                {
                    return null;
                }

                if (module.Contains(address))
                {
                    return module;
                }
            }

            return null;
        }

        private static Level BuildNextLevel(List<RawNode> heapNodes, int perTargetFrontier)
        {
            // Keep the cheapest nodes per target so one variable with a
            // huge number of references can't crowd out the others.
            List<RawNode> kept = new();

            foreach (IGrouping<int, RawNode> group in heapNodes.GroupBy(n => n.Target))
            {
                kept.AddRange(group.OrderBy(n => n.Cost).Take(perTargetFrontier));
            }

            Level level = new()
            {
                Address = new long[kept.Count],
                Parent = new int[kept.Count],
                Offset = new int[kept.Count],
                Target = new int[kept.Count],
                Cost = new int[kept.Count],
                Count = kept.Count
            };

            for (int i = 0; i < kept.Count; i++)
            {
                level.Address[i] = kept[i].Address;
                level.Parent[i] = kept[i].Parent;
                level.Offset[i] = kept[i].Offset;
                level.Target[i] = kept[i].Target;
                level.Cost[i] = kept[i].Cost;
            }

            return level;
        }

        private static void AddCandidate(
            PointerScanResult result,
            List<Level> levels,
            int depth,
            RawNode node,
            ModuleRange module,
            PointerScanOptions options)
        {
            // Chain order: the static node's own offset first, down to the
            // level-1 node's offset last - the order PointerPath expects.
            int[] offsets = new int[depth];
            offsets[0] = node.Offset;

            int parent = node.Parent;
            int sum = node.Offset;

            for (int step = depth - 1; step >= 1; step--)
            {
                Level level = levels[step];
                offsets[depth - step] = level.Offset[parent];
                sum += level.Offset[parent];
                parent = level.Parent[parent];
            }

            int moduleClass = module.IsMain ? 0 : module.IsSystem ? 3 : 1;
            int score = depth * 100_000 + moduleClass * 10_000 + Math.Min(sum, 9_999);

            result.Candidates.Add(new PointerCandidate
            {
                ModuleName = module.Name,
                BaseOffset = node.Address - module.Base,
                Offsets = offsets,
                Score = score
            });

            // Keep memory bounded on pathological targets: trim the worst
            // half once the list is double the cap.
            if (result.Candidates.Count > options.MaxCandidatesPerTarget * 2)
            {
                result.Candidates.Sort((a, b) => a.Score.CompareTo(b.Score));
                result.Candidates.RemoveRange(
                    options.MaxCandidatesPerTarget,
                    result.Candidates.Count - options.MaxCandidatesPerTarget);

                result.Truncated = true;
            }
        }
    }
}
