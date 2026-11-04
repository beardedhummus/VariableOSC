using System.Runtime.InteropServices;
using System.Text;

namespace VariableOSC.SelfTest
{
    /// <summary>
    /// A synthetic "process": a handful of sparse memory blocks plus
    /// modules. Implements both scanner-facing interfaces, so the real
    /// PointerScanner / PointerResolver / PermanenceVerifier run against it
    /// exactly as they would against MemoryScanner on Windows.
    /// </summary>
    public sealed class FakeProcess : IPointerScanMemory, IPointerMemory
    {
        public sealed class Block
        {
            public long Base;
            public byte[] Data = Array.Empty<byte>();
            public bool Scannable;
        }

        private readonly List<Block> blocks = new();

        /// <summary>4 simulates a 32-bit program, 8 a 64-bit one.</summary>
        public int PointerSize { get; set; } = 8;

        public List<ModuleRange> ModuleList { get; } = new();

        public IReadOnlyList<ScanRegion> Regions =>
            blocks.Where(b => b.Scannable).Select(b => new ScanRegion(b.Base, b.Data.Length)).ToList();

        public IReadOnlyList<ModuleRange> Modules => ModuleList;

        public Block AddBlock(long baseAddress, int size, bool scannable)
        {
            Block block = new() { Base = baseAddress, Data = new byte[size], Scannable = scannable };
            blocks.Add(block);
            return block;
        }

        private Block? Find(long address)
        {
            foreach (Block block in blocks)
            {
                if (address >= block.Base && address < block.Base + block.Data.Length)
                {
                    return block;
                }
            }

            return null;
        }

        public bool TryRead(long address, byte[] buffer, int count)
        {
            Block? block = Find(address);

            if (block == null)
            {
                return false;
            }

            long offset = address - block.Base;

            if (offset + count > block.Data.Length)
            {
                return false;
            }

            Buffer.BlockCopy(block.Data, (int)offset, buffer, 0, count);
            return true;
        }

        public bool TryReadPointer(long address, out long value)
        {
            value = 0;
            Block? block = Find(address);

            if (block == null || address + PointerSize > block.Base + block.Data.Length)
            {
                return false;
            }

            int offset = (int)(address - block.Base);
            value = PointerSize == 4 ? BitConverter.ToUInt32(block.Data, offset) : BitConverter.ToInt64(block.Data, offset);
            return true;
        }

        public bool TryGetModuleBase(string moduleName, out long moduleBase)
        {
            ModuleRange? module = ModuleList.FirstOrDefault(m =>
                string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase));

            moduleBase = module?.Base ?? 0;
            return module != null;
        }

        /// <summary>Writes a pointer using the simulated pointer width.</summary>
        public void WritePointer(long address, long value)
        {
            Block block = Find(address) ?? throw new InvalidOperationException($"No block at 0x{address:X}");
            Span<byte> target = block.Data.AsSpan((int)(address - block.Base));

            if (PointerSize == 4)
            {
                BitConverter.TryWriteBytes(target, (uint)value);
            }
            else
            {
                BitConverter.TryWriteBytes(target, value);
            }
        }

        public void WriteInt(long address, int value)
        {
            Block block = Find(address) ?? throw new InvalidOperationException($"No block at 0x{address:X}");
            BitConverter.TryWriteBytes(block.Data.AsSpan((int)(address - block.Base)), value);
        }

        public void WriteAscii(long address, string text)
        {
            Block block = Find(address) ?? throw new InvalidOperationException($"No block at 0x{address:X}");
            byte[] bytes = Encoding.ASCII.GetBytes(text + "\0");
            Buffer.BlockCopy(bytes, 0, block.Data, (int)(address - block.Base), bytes.Length);
        }

        public int? ReadInt(long address)
        {
            Block? block = Find(address);

            if (block == null || address + 4 > block.Base + block.Data.Length)
            {
                return null;
            }

            return BitConverter.ToInt32(block.Data, (int)(address - block.Base));
        }

        public string? ReadAscii(long address, int maxChars)
        {
            Block? block = Find(address);

            if (block == null)
            {
                return null;
            }

            int start = (int)(address - block.Base);
            int length = Math.Min(maxChars, block.Data.Length - start);
            string raw = Encoding.ASCII.GetString(block.Data, start, length);
            int nul = raw.IndexOf('\0');
            return nul >= 0 ? raw[..nul] : raw;
        }

        /// <summary>Fills a block with junk: a share of the pointer-width words look like heap pointers, the rest like small numbers.</summary>
        public void FillNoise(Block block, Random rnd, long pointerRangeBase, long pointerRangeSize, int pointerPercent)
        {
            if (PointerSize == 4)
            {
                Span<uint> words32 = MemoryMarshal.Cast<byte, uint>(block.Data.AsSpan());

                for (int i = 0; i < words32.Length; i++)
                {
                    words32[i] = rnd.Next(100) < pointerPercent
                        ? (uint)(pointerRangeBase + (rnd.NextInt64(pointerRangeSize) & ~3L))
                        : (uint)rnd.Next(0, 5000);
                }

                return;
            }

            Span<long> words = MemoryMarshal.Cast<byte, long>(block.Data.AsSpan());

            for (int i = 0; i < words.Length; i++)
            {
                words[i] = rnd.Next(100) < pointerPercent
                    ? pointerRangeBase + (rnd.NextInt64(pointerRangeSize) & ~7L)
                    : rnd.Next(0, 5000);
            }
        }
    }
}
