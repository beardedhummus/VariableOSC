using System;
using System.Collections.Generic;

namespace VariableOSC
{
    /// <summary>A committed, readable stretch of the target's memory worth searching for pointers.</summary>
    public readonly record struct ScanRegion(long Base, long Size);

    /// <summary>One loaded module (EXE or DLL). Anything inside one is "static": it keeps the same offset from the module base on every launch.</summary>
    public sealed class ModuleRange
    {
        public string Name { get; init; } = "";
        public long Base { get; init; }
        public long Size { get; init; }
        public bool IsMain { get; init; }
        public bool IsSystem { get; init; }

        public bool Contains(long address) => address >= Base && address < Base + Size;
    }

    /// <summary>
    /// What the pointer scanner needs from a process. Kept as an interface
    /// so the algorithm can be tested against a synthetic in-memory
    /// "process" without Windows - MemoryScanner supplies the real one.
    /// </summary>
    public interface IPointerScanMemory
    {
        /// <summary>Size of a pointer in the target: 4 for a 32-bit program, 8 for 64-bit.</summary>
        int PointerSize { get; }

        IReadOnlyList<ScanRegion> Regions { get; }

        IReadOnlyList<ModuleRange> Modules { get; }

        /// <summary>Reads exactly count bytes into the start of buffer. False if any part is unreadable.</summary>
        bool TryRead(long address, byte[] buffer, int count);
    }

    /// <summary>What chain resolution needs: read one pointer, look up a module's current base.</summary>
    public interface IPointerMemory
    {
        /// <summary>Size of a pointer in the target: 4 for a 32-bit program, 8 for 64-bit.</summary>
        int PointerSize { get; }

        bool TryReadPointer(long address, out long value);

        bool TryGetModuleBase(string moduleName, out long moduleBase);
    }

    public sealed class PointerScanOptions
    {
        /// <summary>Maximum number of pointer hops from a static address to the variable.</summary>
        public int MaxLevels { get; set; } = 4;

        /// <summary>Largest offset added after dereferencing a pointer (how far into an object a field may sit).</summary>
        public int MaxOffset { get; set; } = 0x400;

        /// <summary>Offsets must be a multiple of this (fields are almost always 4-byte aligned).</summary>
        public int Alignment { get; set; } = 4;

        /// <summary>Most nodes carried into the next level; the cheapest (smallest offsets) are kept.</summary>
        public int MaxFrontier { get; set; } = 150_000;

        /// <summary>Hard cap on new nodes created in one pass, as a memory safety valve.</summary>
        public int MaxNodesPerLevel { get; set; } = 600_000;

        /// <summary>Best-ranked chains kept per target.</summary>
        public int MaxCandidatesPerTarget { get; set; } = 10_000;

        public int ChunkBytes { get; set; } = 1 << 20;
    }

    public readonly record struct PointerScanProgress(
        int Level,
        int MaxLevels,
        double LevelFraction,
        long NodesFound,
        long StaticPathsFound);

    public sealed class PointerCandidate
    {
        public string ModuleName { get; init; } = "";
        public long BaseOffset { get; init; }
        public int[] Offsets { get; init; } = Array.Empty<int>();
        /// <summary>Lower is better.</summary>
        public int Score { get; init; }
    }

    /// <summary>What one pass of the scan found for one variable: heap slots that point at the previous hop, and finished chains that reached a module.</summary>
    public readonly record struct ScanHopStat(int Hop, int Links, int Chains);

    public sealed class PointerScanResult
    {
        public long TargetAddress { get; init; }
        public List<PointerCandidate> Candidates { get; } = new();
        public bool Truncated { get; set; }

        /// <summary>One entry per hop (1-based), padded with zeros if the trail ended early. Explains an empty result.</summary>
        public List<ScanHopStat> Hops { get; } = new();

        public int PointerSize { get; set; } = 8;
    }

    public enum PermanenceHealth
    {
        NotApplicable,
        Good,
        Degraded,
        Lost
    }

    public readonly record struct ConsensusResult(
        long Address,
        PermanenceHealth Health,
        int Agreeing,
        int Resolved,
        int Total);
}
