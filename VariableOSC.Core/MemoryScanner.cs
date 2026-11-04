using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace VariableOSC
{
    public class MemoryScanner : IPointerMemory
    {
        private readonly Process process;
        private readonly IntPtr processHandle;

        /// <summary>
        /// The PID this scanner is attached to. Lets a caller that only
        /// has a MemoryVariable (which stores a Scanner but not a PID)
        /// re-open a scan screen against the same process - e.g. Replace
        /// Target.
        /// </summary>
        public int ProcessId => process.Id;

        /// <summary>Process name as Process.GetProcessesByName expects it (no ".exe").</summary>
        public string ProcessName { get; }

        /// <summary>When the target started (UTC ticks), or 0 if it couldn't be read. A different value for the same program means it has been restarted.</summary>
        public long StartTimeUtcTicks { get; }

        /// <summary>Main-module file version of the target, used to notice when an app update may have moved things.</summary>
        public string TargetVersion { get; }

        /// <summary>4 when the target is a 32-bit program (running under WOW64), otherwise 8. Decides how wide a pointer is when following chains.</summary>
        public int PointerSize { get; }

        /// <summary>True once the target process has exited. After that every read fails until the variables are re-attached to the relaunched process.</summary>
        public bool HasExited
        {
            get
            {
                // Ask through the handle this class already holds rather
                // than the Process object, which can refuse (access denied)
                // for an elevated target even though reading memory works.
                if (GetExitCodeProcess(processHandle, out uint exitCode))
                {
                    return exitCode != STILL_ACTIVE;
                }

                return SafeGet(() => process.HasExited, true);
            }
        }

        private static T SafeGet<T>(Func<T> getter, T fallback)
        {
            try
            {
                return getter();
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;

        private const uint MEM_COMMIT = 0x1000;

        private const uint PAGE_READONLY = 0x02;
        private const uint PAGE_READWRITE = 0x04;
        private const uint PAGE_WRITECOPY = 0x08;
        private const uint PAGE_EXECUTE_READ = 0x20;
        private const uint PAGE_EXECUTE_READWRITE = 0x40;
        private const uint PAGE_EXECUTE_WRITECOPY = 0x80;
        private const uint PAGE_GUARD = 0x100;

        private const uint STILL_ACTIVE = 259;

        private const uint MEM_PRIVATE = 0x20000;
        private const uint MEM_IMAGE = 0x1000000;

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public UIntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(
            uint dwDesiredAccess,
            bool bInheritHandle,
            int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            byte[] lpBuffer,
            UIntPtr nSize,
            out UIntPtr lpNumberOfBytesRead);

        // Same Win32 call, but reads straight into a long so following a
        // pointer chain doesn't allocate a byte[] per hop.
        [DllImport("kernel32.dll", EntryPoint = "ReadProcessMemory", SetLastError = true)]
        private static extern bool ReadProcessMemoryInt64(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            out long lpBuffer,
            UIntPtr nSize,
            out UIntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll")]
        private static extern int VirtualQueryEx(
            IntPtr hProcess,
            IntPtr lpAddress,
            out MEMORY_BASIC_INFORMATION lpBuffer,
            uint dwLength);

        // 32-bit pointers: reads straight into a uint.
        [DllImport("kernel32.dll", EntryPoint = "ReadProcessMemory", SetLastError = true)]
        private static extern bool ReadProcessMemoryUInt32(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            out uint lpBuffer,
            UIntPtr nSize,
            out UIntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsWow64Process(IntPtr hProcess, out bool wow64Process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        public MemoryScanner(Process process)
        {
            this.process = process;

            // Captured up front: once the target exits, reading any of
            // these from the Process object throws.
            ProcessName = SafeGet(() => process.ProcessName, "");
            StartTimeUtcTicks = SafeGet(() => process.StartTime.ToUniversalTime().Ticks, 0L);
            TargetVersion = SafeGet(() => process.MainModule?.FileVersionInfo.FileVersion ?? "", "");

            processHandle = OpenProcess(
                PROCESS_VM_READ | PROCESS_QUERY_INFORMATION,
                false,
                process.Id);

            if (processHandle == IntPtr.Zero)
            {
                throw new Exception(
                    "Could not open the target process.");
            }

            // This app is 64-bit, so a target flagged WOW64 is a 32-bit
            // program: its pointers are 4 bytes wide.
            PointerSize = IsWow64Process(processHandle, out bool isWow64) && isWow64 ? 4 : 8;
        }

        public bool ReadMemory(
            IntPtr address,
            byte[] buffer)
        {
            return ReadProcessMemory(
                processHandle,
                address,
                buffer,
                (UIntPtr)buffer.Length,
                out UIntPtr bytesRead)
                &&
                bytesRead.ToUInt64() ==
                (ulong)buffer.Length;
        }

        public List<IntPtr> FirstScan(
            object searchValue,
            DataType dataType)
        {
            byte[] pattern = ConvertValueToBytes(
                searchValue,
                dataType);

            List<IntPtr> results = new List<IntPtr>();

            IntPtr address = IntPtr.Zero;

            while (true)
            {
                MEMORY_BASIC_INFORMATION mbi;

                int result = VirtualQueryEx(
                    processHandle,
                    address,
                    out mbi,
                    (uint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>());

                if (result == 0)
                    break;

                bool committed =
                    mbi.State == MEM_COMMIT;

                bool readable =
                    IsReadable(mbi.Protect);

                if (committed && readable)
                {
                    long regionSize =
                        (long)mbi.RegionSize;

                    if (regionSize > 0 &&
                        regionSize <= int.MaxValue)
                    {
                        byte[] buffer =
                            new byte[(int)regionSize];

                        if (ReadProcessMemory(
                            processHandle,
                            mbi.BaseAddress,
                            buffer,
                            (UIntPtr)buffer.Length,
                            out UIntPtr bytesRead))
                        {
                            int bytes =
                                (int)bytesRead.ToUInt64();

                            SearchBuffer(
                                buffer,
                                bytes,
                                pattern,
                                mbi.BaseAddress,
                                results);
                        }
                    }
                }

                long next =
                    mbi.BaseAddress.ToInt64() +
                    (long)mbi.RegionSize;

                if (next <= address.ToInt64())
                    break;

                address = new IntPtr(next);
            }

            return results;
        }

        public List<IntPtr> NextScan(
            List<IntPtr> candidates,
            object searchValue,
            DataType dataType)
        {
            byte[] pattern =
                ConvertValueToBytes(
                    searchValue,
                    dataType);

            List<IntPtr> results =
                new List<IntPtr>();

            foreach (IntPtr address in candidates)
            {
                byte[] buffer =
                    new byte[pattern.Length];

                if (ReadProcessMemory(
                    processHandle,
                    address,
                    buffer,
                    (UIntPtr)buffer.Length,
                    out UIntPtr bytesRead))
                {
                    if (bytesRead.ToUInt64() ==
                        (ulong)pattern.Length)
                    {
                        bool match = true;

                        for (int i = 0;
                             i < pattern.Length;
                             i++)
                        {
                            if (buffer[i] != pattern[i])
                            {
                                match = false;
                                break;
                            }
                        }

                        if (match)
                            results.Add(address);
                    }
                }
            }

            return results;
        }

        private static readonly DataType[] NumericTypes =
        {
            DataType.Byte,
            DataType.Int16,
            DataType.Int32,
            DataType.Int64,
            DataType.Float,
            DataType.Double
        };

        /// <summary>
        /// Runs a first scan across every numeric type the entered value
        /// can be interpreted as (see Program.TryParseAnyNumber), so the
        /// user doesn't have to know or pick the target's storage type up
        /// front. Each match is tagged with the specific type it matched.
        /// </summary>
        public List<NumberMatch> FirstScanAnyNumber(
            Dictionary<DataType, object> valuesByType)
        {
            List<NumberMatch> results = new List<NumberMatch>();

            foreach (DataType type in NumericTypes)
            {
                if (!valuesByType.TryGetValue(type, out object? value))
                    continue;

                List<IntPtr> addresses = FirstScan(value, type);

                foreach (IntPtr address in addresses)
                {
                    results.Add(new NumberMatch { Address = address, Type = type });
                }
            }

            return results;
        }

        /// <summary>
        /// Narrows an existing combined-number candidate list using a new
        /// value. Each candidate is re-checked only against its own
        /// previously-matched type; a candidate is dropped if the new
        /// value can't be interpreted as that type (e.g. a decimal value
        /// entered while narrowing a Byte/Int16/Int32/Int64 candidate).
        /// </summary>
        public List<NumberMatch> NextScanAnyNumber(
            List<NumberMatch> candidates,
            Dictionary<DataType, object> valuesByType)
        {
            List<NumberMatch> results = new List<NumberMatch>();

            foreach (NumberMatch candidate in candidates)
            {
                if (!valuesByType.TryGetValue(candidate.Type, out object? value))
                    continue;

                List<IntPtr> matched = NextScan(
                    new List<IntPtr> { candidate.Address },
                    value,
                    candidate.Type);

                if (matched.Count > 0)
                    results.Add(candidate);
            }

            return results;
        }

        public object? ReadValue(
            IntPtr address,
            DataType dataType)
        {
            int size =
                GetDataSize(dataType);

            if (size <= 0)
                return null;

            byte[] buffer =
                new byte[size];

            if (!ReadProcessMemory(
                processHandle,
                address,
                buffer,
                (UIntPtr)buffer.Length,
                out UIntPtr bytesRead))
            {
                return null;
            }

            if (bytesRead.ToUInt64() !=
                (ulong)buffer.Length)
            {
                return null;
            }

            return ConvertBytesToValue(
                buffer,
                dataType);
        }

        /// <summary>
        /// Reads a saved variable's current value, dispatching to the
        /// string reader when needed. This is the entry point OSC jobs
        /// and the live monitor use, so they don't need to know the
        /// difference between numeric and string variables.
        ///
        /// If the variable carries a PointerPath, it's re-resolved on
        /// every call (not just once) - that's what lets a variable keep
        /// working even if an intermediate pointer in the chain moves
        /// during the session (e.g. the target reallocated a heap block),
        /// not just across a full restart. variable.Address is kept in
        /// sync with the resolved address purely for display purposes.
        /// </summary>
        public object? ReadValue(MemoryVariable variable)
        {
            IntPtr address = variable.Address;
            bool learned = variable.PermanentPaths.Count > 0;

            if (learned)
            {
                // Make Permanent chains: resolve all of them and let them
                // vote. Nothing resolving, or no clear majority, reads as
                // "not found" so a broadcast pauses instead of sending
                // whatever happens to be at a stale address.
                ConsensusResult consensus =
                    PointerResolver.ResolveConsensus(this, variable.PermanentPaths);

                variable.LastConsensus = consensus;

                if (consensus.Health == PermanenceHealth.Lost)
                {
                    return null;
                }

                address = new IntPtr(consensus.Address);
                variable.Address = address;
            }
            else if (variable.PointerPath == null &&
                variable.PermanenceState == PermanenceState.Learned &&
                PermanenceWorkflow.HasRestartedSinceLearning(variable))
            {
                // Learned but not yet verified, and the target has been
                // relaunched: the saved raw address now points at unrelated
                // memory, so report "not found" instead of garbage until
                // the restart check finishes.
                return null;
            }
            else if (variable.PointerPath != null)
            {
                IntPtr? resolved = ResolvePointerPath(variable.PointerPath);

                if (resolved == null)
                {
                    return null;
                }

                address = resolved.Value;
                variable.Address = address;
            }

            object? value = variable.Type == DataType.String
                ? ReadString(address, variable.StringEncoding, variable.StringLength)
                : ReadValue(address, variable.Type);

            if (learned && !ValuePlausibility.IsPlausible(value))
            {
                variable.LastConsensus = variable.LastConsensus with { Health = PermanenceHealth.Lost };

                return null;
            }

            return value;
        }

        // ------------------------------------------------------------
        // Module + pointer-path resolution. See PointerPath.cs for why
        // this exists: a module-relative offset (and, for heap variables,
        // an offset chain walked through intermediate pointers) survives a
        // restart of the target process, unlike a raw absolute address.
        // ------------------------------------------------------------

        /// <summary>
        /// Finds the loaded module (EXE or DLL) containing the given
        /// address, if any. Used to auto-detect whether a freshly-found
        /// candidate can become a simple module+offset PointerPath.
        /// </summary>
        /// <summary>
        /// Snapshots the target's loaded modules. Process.Modules enumerates
        /// via its own separate Win32 calls (independent of the raw handle
        /// this class opened for ReadProcessMemory), and can throw - e.g. a
        /// Win32Exception if the target is a packaged/sandboxed app with
        /// tighter access restrictions than a plain desktop process. Every
        /// caller here treats "couldn't enumerate modules" as "found
        /// nothing" rather than letting the exception escape and take the
        /// whole app down with it.
        /// </summary>
        private List<ProcessModule> SafeListModules()
        {
            try
            {
                return process.Modules.Cast<ProcessModule>().ToList();
            }
            catch
            {
                return new List<ProcessModule>();
            }
        }

        public ProcessModule? FindModuleContaining(IntPtr address)
        {
            long target = address.ToInt64();

            foreach (ProcessModule module in SafeListModules())
            {
                long baseAddress = module.BaseAddress.ToInt64();
                long size = module.ModuleMemorySize;

                if (target >= baseAddress && target < baseAddress + size)
                {
                    return module;
                }
            }

            return null;
        }

        /// <summary>
        /// Lists every loaded module's name, for building a manual pointer
        /// chain when the target module isn't the one the address was
        /// originally found in.
        /// </summary>
        public List<string> ListModuleNames()
        {
            return SafeListModules()
                .Select(m => m.ModuleName)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public IntPtr? GetModuleBaseAddress(string moduleName)
        {
            return TryGetModuleBase(moduleName, out long moduleBase)
                ? new IntPtr(moduleBase)
                : null;
        }

        // ------------------------------------------------------------
        // Module cache. Module lookups used to enumerate every loaded DLL
        // on every single read; with several chains per variable polled
        // ten times a second that was needlessly heavy. A module's base
        // never changes while the process lives, so remember it and only
        // re-enumerate (at most every couple of seconds) on a miss.
        // ------------------------------------------------------------

        private const long ModuleCacheMilliseconds = 2000;

        private readonly object moduleLock = new();
        private List<ModuleRange> moduleRanges = new();
        private Dictionary<string, long> moduleBases = new(StringComparer.OrdinalIgnoreCase);
        private long moduleCacheTicks;

        private void RefreshModuleCacheLocked()
        {
            moduleCacheTicks = Environment.TickCount64;

            List<ProcessModule> modules;

            try
            {
                process.Refresh();
                modules = process.Modules.Cast<ProcessModule>().ToList();
            }
            catch (Exception)
            {
                // Keep whatever was cached; a failed enumeration (target
                // exiting, restricted process) shouldn't erase good data.
                return;
            }

            long mainBase = SafeGet(() => process.MainModule?.BaseAddress.ToInt64() ?? 0L, 0L);
            string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            List<ModuleRange> ranges = new();
            Dictionary<string, long> bases = new(StringComparer.OrdinalIgnoreCase);

            foreach (ProcessModule module in modules)
            {
                long moduleBase = module.BaseAddress.ToInt64();
                string fileName = SafeGet(() => module.FileName ?? "", "");

                ranges.Add(new ModuleRange
                {
                    Name = module.ModuleName,
                    Base = moduleBase,
                    Size = module.ModuleMemorySize,
                    IsMain = moduleBase == mainBase,
                    IsSystem = windowsDirectory.Length > 0 &&
                        fileName.StartsWith(windowsDirectory, StringComparison.OrdinalIgnoreCase)
                });

                bases[module.ModuleName] = moduleBase;
            }

            moduleRanges = ranges;
            moduleBases = bases;
        }

        public bool TryGetModuleBase(string moduleName, out long moduleBase)
        {
            lock (moduleLock)
            {
                if (moduleBases.TryGetValue(moduleName, out moduleBase))
                {
                    return true;
                }

                if (Environment.TickCount64 - moduleCacheTicks > ModuleCacheMilliseconds)
                {
                    RefreshModuleCacheLocked();

                    if (moduleBases.TryGetValue(moduleName, out moduleBase))
                    {
                        return true;
                    }
                }
            }

            moduleBase = 0;
            return false;
        }

        /// <summary>Fresh snapshot of the loaded modules with size and main/system flags, for the pointer scanner.</summary>
        public List<ModuleRange> SnapshotModules()
        {
            lock (moduleLock)
            {
                RefreshModuleCacheLocked();
                return new List<ModuleRange>(moduleRanges);
            }
        }

        public bool TryReadPointer(long address, out long value)
        {
            value = 0;

            if (PointerSize == 4)
            {
                if (!ReadProcessMemoryUInt32(
                        processHandle,
                        new IntPtr(address),
                        out uint narrow,
                        (UIntPtr)4,
                        out UIntPtr narrowRead) ||
                    narrowRead.ToUInt64() != 4)
                {
                    return false;
                }

                value = narrow;
                return true;
            }

            return ReadProcessMemoryInt64(
                processHandle,
                new IntPtr(address),
                out value,
                (UIntPtr)8,
                out UIntPtr bytesRead)
                && bytesRead.ToUInt64() == 8;
        }

        public IntPtr? ResolvePointerPath(PointerPath path)
        {
            return PointerResolver.TryResolve(this, path, out long address)
                ? new IntPtr(address)
                : null;
        }

        // ------------------------------------------------------------
        // Pointer scanning support (see PointerScanner)
        // ------------------------------------------------------------

        /// <summary>
        /// Snapshot of this process in the shape the pointer scanner wants:
        /// its writable private/image regions (where a pointer to live data
        /// can be stored) plus its modules. Read-only, executable-only,
        /// guard and memory-mapped regions are left out since they can't
        /// hold a pointer that changes at runtime.
        /// </summary>
        public IPointerScanMemory CreateScanMemory()
        {
            return new ScanMemory(this, SnapshotModules());
        }

        private sealed class ScanMemory : IPointerScanMemory
        {
            private readonly MemoryScanner owner;

            public int PointerSize => owner.PointerSize;

            public IReadOnlyList<ScanRegion> Regions { get; }

            public IReadOnlyList<ModuleRange> Modules { get; }

            public ScanMemory(MemoryScanner owner, List<ModuleRange> modules)
            {
                this.owner = owner;
                Modules = modules;

                List<ScanRegion> regions = new();
                long address = 0;

                // A 32-bit program can't hold a pointer above 4 GB, so don't
                // waste time on the 64-bit system DLLs mapped up there.
                long limit = owner.PointerSize == 4 ? 0x1_0000_0000L : 0x7FFF_FFFF_FFFF;

                while (address >= 0 && address < limit)
                {
                    int result = VirtualQueryEx(
                        owner.processHandle,
                        new IntPtr(address),
                        out MEMORY_BASIC_INFORMATION mbi,
                        (uint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>());

                    if (result == 0)
                    {
                        break;
                    }

                    long regionBase = mbi.BaseAddress.ToInt64();
                    long regionSize = (long)mbi.RegionSize;

                    if (regionSize <= 0)
                    {
                        break;
                    }

                    bool writable =
                        (mbi.Protect & (PAGE_READWRITE | PAGE_WRITECOPY |
                            PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY)) != 0;

                    bool usable =
                        mbi.State == MEM_COMMIT &&
                        writable &&
                        (mbi.Protect & PAGE_GUARD) == 0 &&
                        (mbi.Type == MEM_PRIVATE || mbi.Type == MEM_IMAGE);

                    if (usable)
                    {
                        regions.Add(new ScanRegion(regionBase, regionSize));
                    }

                    address = regionBase + regionSize;
                }

                Regions = regions;
            }

            public bool TryRead(long address, byte[] buffer, int count)
            {
                return ReadProcessMemory(
                    owner.processHandle,
                    new IntPtr(address),
                    buffer,
                    (UIntPtr)count,
                    out UIntPtr bytesRead)
                    && bytesRead.ToUInt64() == (ulong)count;
            }
        }

        public string? ReadString(
            IntPtr address,
            StringEncodingType encoding,
            int maxChars)
        {
            if (maxChars <= 0)
                return null;

            int bytesPerChar =
                encoding == StringEncodingType.Ascii ? 1 : 2;

            int byteLength = maxChars * bytesPerChar;

            byte[] buffer = new byte[byteLength];

            if (!ReadProcessMemory(
                processHandle,
                address,
                buffer,
                (UIntPtr)buffer.Length,
                out UIntPtr bytesRead) ||
                bytesRead.ToUInt64() != (ulong)buffer.Length)
            {
                return null;
            }

            string raw =
                encoding == StringEncodingType.Ascii
                    ? Encoding.ASCII.GetString(buffer)
                    : Encoding.Unicode.GetString(buffer);

            int nullIndex = raw.IndexOf('\0');

            return nullIndex >= 0
                ? raw.Substring(0, nullIndex)
                : raw;
        }

        public List<StringMatch> FirstScanString(
            string searchValue,
            StringSearchOptions options)
        {
            List<StringMatch> results = new List<StringMatch>();

            if (string.IsNullOrEmpty(searchValue))
                return results;

            IntPtr address = IntPtr.Zero;

            while (true)
            {
                MEMORY_BASIC_INFORMATION mbi;

                int result = VirtualQueryEx(
                    processHandle,
                    address,
                    out mbi,
                    (uint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>());

                if (result == 0)
                    break;

                bool committed =
                    mbi.State == MEM_COMMIT;

                bool readable =
                    IsReadable(mbi.Protect);

                if (committed && readable)
                {
                    long regionSize =
                        (long)mbi.RegionSize;

                    if (regionSize > 0 &&
                        regionSize <= int.MaxValue)
                    {
                        byte[] buffer =
                            new byte[(int)regionSize];

                        if (ReadProcessMemory(
                            processHandle,
                            mbi.BaseAddress,
                            buffer,
                            (UIntPtr)buffer.Length,
                            out UIntPtr bytesRead))
                        {
                            int bytes =
                                (int)bytesRead.ToUInt64();

                            SearchBufferForString(
                                buffer,
                                bytes,
                                searchValue,
                                options,
                                mbi.BaseAddress,
                                results);
                        }
                    }
                }

                long next =
                    mbi.BaseAddress.ToInt64() +
                    (long)mbi.RegionSize;

                if (next <= address.ToInt64())
                    break;

                address = new IntPtr(next);
            }

            return results;
        }

        public List<StringMatch> NextScanString(
            List<StringMatch> candidates,
            string searchValue,
            StringSearchOptions options)
        {
            List<StringMatch> results = new List<StringMatch>();

            foreach (StringMatch candidate in candidates)
            {
                string? current = ReadString(
                    candidate.Address,
                    candidate.Encoding,
                    searchValue.Length);

                if (current == null)
                    continue;

                bool matches = options.CaseSensitive
                    ? current == searchValue
                    : current.Equals(
                        searchValue,
                        StringComparison.OrdinalIgnoreCase);

                if (!matches)
                    continue;

                if (options.NullTerminated &&
                    !HasTerminatorAfter(
                        candidate.Address,
                        candidate.Encoding,
                        searchValue.Length))
                {
                    continue;
                }

                results.Add(candidate);
            }

            return results;
        }

        private bool HasTerminatorAfter(
            IntPtr matchAddress,
            StringEncodingType encoding,
            int charLength)
        {
            int terminatorSize =
                encoding == StringEncodingType.Ascii ? 1 : 2;

            int byteOffset =
                encoding == StringEncodingType.Ascii
                    ? charLength
                    : charLength * 2;

            byte[] termBuffer = new byte[terminatorSize];

            bool ok = ReadProcessMemory(
                processHandle,
                IntPtr.Add(matchAddress, byteOffset),
                termBuffer,
                (UIntPtr)termBuffer.Length,
                out UIntPtr termBytesRead);

            return ok &&
                termBytesRead.ToUInt64() == (ulong)termBuffer.Length &&
                !termBuffer.Any(b => b != 0);
        }

        private static void SearchBufferForString(
            byte[] buffer,
            int bytesRead,
            string searchValue,
            StringSearchOptions options,
            IntPtr baseAddress,
            List<StringMatch> results)
        {
            int patternLength = searchValue.Length;

            if (patternLength == 0)
                return;

            for (int i = 0; i < bytesRead; i++)
            {
                if (i + patternLength <= bytesRead &&
                    MatchesAscii(buffer, i, searchValue, options.CaseSensitive))
                {
                    if (!options.NullTerminated ||
                        IsAsciiTerminated(buffer, i + patternLength, bytesRead))
                    {
                        results.Add(new StringMatch
                        {
                            Address = IntPtr.Add(baseAddress, i),
                            Encoding = StringEncodingType.Ascii,
                            MatchedCharLength = patternLength
                        });
                    }
                }

                if (i + (patternLength * 2) <= bytesRead &&
                    MatchesUtf16(buffer, i, searchValue, options.CaseSensitive))
                {
                    if (!options.NullTerminated ||
                        IsUtf16Terminated(buffer, i + (patternLength * 2), bytesRead))
                    {
                        results.Add(new StringMatch
                        {
                            Address = IntPtr.Add(baseAddress, i),
                            Encoding = StringEncodingType.Utf16,
                            MatchedCharLength = patternLength
                        });
                    }
                }
            }
        }

        private static bool MatchesAscii(
            byte[] buffer,
            int offset,
            string pattern,
            bool caseSensitive)
        {
            for (int k = 0; k < pattern.Length; k++)
            {
                char c = (char)buffer[offset + k];

                if (!CharsMatch(c, pattern[k], caseSensitive))
                    return false;
            }

            return true;
        }

        private static bool MatchesUtf16(
            byte[] buffer,
            int offset,
            string pattern,
            bool caseSensitive)
        {
            for (int k = 0; k < pattern.Length; k++)
            {
                int byteOffset = offset + (k * 2);

                char c = (char)(
                    buffer[byteOffset] |
                    (buffer[byteOffset + 1] << 8));

                if (!CharsMatch(c, pattern[k], caseSensitive))
                    return false;
            }

            return true;
        }

        private static bool CharsMatch(
            char a,
            char b,
            bool caseSensitive)
        {
            if (caseSensitive)
                return a == b;

            return char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
        }

        private static bool IsAsciiTerminated(
            byte[] buffer,
            int offset,
            int bytesRead)
        {
            return offset < bytesRead && buffer[offset] == 0;
        }

        private static bool IsUtf16Terminated(
            byte[] buffer,
            int offset,
            int bytesRead)
        {
            return offset + 1 < bytesRead &&
                buffer[offset] == 0 &&
                buffer[offset + 1] == 0;
        }

        private static bool IsReadable(
            uint protection)
        {
            if ((protection & PAGE_READONLY) != 0)
                return true;

            if ((protection & PAGE_READWRITE) != 0)
                return true;

            if ((protection & PAGE_WRITECOPY) != 0)
                return true;

            if ((protection & PAGE_EXECUTE_READ) != 0)
                return true;

            if ((protection & PAGE_EXECUTE_READWRITE) != 0)
                return true;

            if ((protection & PAGE_EXECUTE_WRITECOPY) != 0)
                return true;

            return false;
        }

        private static void SearchBuffer(
            byte[] buffer,
            int bytesRead,
            byte[] pattern,
            IntPtr baseAddress,
            List<IntPtr> results)
        {
            if (pattern.Length == 0)
                return;

            for (int i = 0;
                 i <= bytesRead - pattern.Length;
                 i++)
            {
                bool match = true;

                for (int j = 0;
                     j < pattern.Length;
                     j++)
                {
                    if (buffer[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    IntPtr address =
                        IntPtr.Add(
                            baseAddress,
                            i);

                    results.Add(address);
                }
            }
        }

        private static int GetDataSize(
            DataType dataType)
        {
            return dataType switch
            {
                DataType.Byte => 1,
                DataType.Int16 => 2,
                DataType.Int32 => 4,
                DataType.Int64 => 8,
                DataType.Float => 4,
                DataType.Double => 8,
                DataType.Bool => 1,
                _ => 0
            };
        }

        private static byte[] ConvertValueToBytes(
            object value,
            DataType dataType)
        {
            return dataType switch
            {
                DataType.Byte =>
                    new[] { Convert.ToByte(value) },

                DataType.Int16 =>
                    BitConverter.GetBytes(
                        Convert.ToInt16(value)),

                DataType.Int32 =>
                    BitConverter.GetBytes(
                        Convert.ToInt32(value)),

                DataType.Int64 =>
                    BitConverter.GetBytes(
                        Convert.ToInt64(value)),

                DataType.Float =>
                    BitConverter.GetBytes(
                        Convert.ToSingle(value)),

                DataType.Double =>
                    BitConverter.GetBytes(
                        Convert.ToDouble(value)),

                DataType.Bool =>
                    BitConverter.GetBytes(
                        Convert.ToBoolean(value)),

                _ => Encoding.UTF8.GetBytes(
                    Convert.ToString(value) ?? "")
            };
        }

        private static object ConvertBytesToValue(
            byte[] bytes,
            DataType dataType)
        {
            return dataType switch
            {
                DataType.Byte =>
                    bytes[0],

                DataType.Int16 =>
                    BitConverter.ToInt16(bytes, 0),

                DataType.Int32 =>
                    BitConverter.ToInt32(bytes, 0),

                DataType.Int64 =>
                    BitConverter.ToInt64(bytes, 0),

                DataType.Float =>
                    BitConverter.ToSingle(bytes, 0),

                DataType.Double =>
                    BitConverter.ToDouble(bytes, 0),

                DataType.Bool =>
                    BitConverter.ToBoolean(bytes, 0),

                _ =>
                    Encoding.UTF8.GetString(bytes)
            };
        }

        public void Close()
        {
            if (processHandle != IntPtr.Zero)
            {
                CloseHandle(processHandle);
            }
        }
    }
}
