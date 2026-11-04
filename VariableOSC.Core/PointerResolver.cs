using System;
using System.Collections.Generic;

namespace VariableOSC
{
    /// <summary>
    /// Walks saved pointer chains against a live process, and turns many
    /// chains for one variable into a single trustworthy answer (consensus)
    /// plus a health rating. Pure logic over IPointerMemory so it can be
    /// exercised without a real process.
    /// </summary>
    public static class PointerResolver
    {
        private const long MinUserAddress = 0x10000;
        private const long MaxUserAddress64 = 0x7FFF_FFFF_FFFF;
        private const long MaxUserAddress32 = 0xFFFF_FFFF;

        public static bool TryResolve(IPointerMemory memory, PointerPath path, out long address)
        {
            address = 0;

            if (!memory.TryGetModuleBase(path.ModuleName, out long moduleBase))
            {
                return false;
            }

            long maxAddress = memory.PointerSize == 4 ? MaxUserAddress32 : MaxUserAddress64;
            long current = moduleBase + path.BaseOffset;

            if (!path.IsPointerChain)
            {
                address = current;
                return true;
            }

            if (!memory.TryReadPointer(current, out current))
            {
                return false;
            }

            for (int i = 0; i < path.Offsets.Count; i++)
            {
                if (!LooksLikePointer(current, maxAddress))
                {
                    return false;
                }

                long next = current + path.Offsets[i];

                if (i == path.Offsets.Count - 1)
                {
                    address = next;
                    return LooksLikePointer(next, maxAddress);
                }

                if (!memory.TryReadPointer(next, out current))
                {
                    return false;
                }
            }

            address = current;
            return LooksLikePointer(current, maxAddress);
        }

        private static bool LooksLikePointer(long value, long maxAddress) =>
            value >= MinUserAddress && value <= maxAddress;

        /// <summary>
        /// Resolves every chain and lets them vote. All resolving chains
        /// agreeing is Good; a strict majority agreeing is Degraded (some
        /// chains broke, e.g. after an app update); anything else is Lost,
        /// which tells the caller to stop sending rather than send a
        /// guess.
        /// </summary>
        public static ConsensusResult ResolveConsensus(IPointerMemory memory, IReadOnlyList<PointerPath> paths)
        {
            int total = paths.Count;

            if (total == 0)
            {
                return new ConsensusResult(0, PermanenceHealth.NotApplicable, 0, 0, 0);
            }

            Span<long> resolvedAddresses = total <= 32 ? stackalloc long[total] : new long[total];
            int resolved = 0;

            for (int i = 0; i < total; i++)
            {
                if (TryResolve(memory, paths[i], out long address))
                {
                    resolvedAddresses[resolved++] = address;
                }
            }

            if (resolved == 0)
            {
                return new ConsensusResult(0, PermanenceHealth.Lost, 0, 0, total);
            }

            resolvedAddresses = resolvedAddresses.Slice(0, resolved);
            resolvedAddresses.Sort();

            long bestAddress = 0;
            int bestCount = 0;
            bool tie = false;
            int run = 0;

            for (int i = 0; i < resolvedAddresses.Length; i++)
            {
                run = i > 0 && resolvedAddresses[i] == resolvedAddresses[i - 1] ? run + 1 : 1;

                if (run > bestCount)
                {
                    bestCount = run;
                    bestAddress = resolvedAddresses[i];
                    tie = false;
                }
                else if (run == bestCount && resolvedAddresses[i] != bestAddress)
                {
                    tie = true;
                }
            }

            bool strictMajority = bestCount * 2 > resolved;

            if (!strictMajority || tie)
            {
                return new ConsensusResult(0, PermanenceHealth.Lost, bestCount, resolved, total);
            }

            PermanenceHealth health =
                bestCount == resolved && bestCount * 2 >= total
                    ? PermanenceHealth.Good
                    : PermanenceHealth.Degraded;

            return new ConsensusResult(bestAddress, health, bestCount, resolved, total);
        }
    }
}
