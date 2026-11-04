using System.Collections.Generic;

namespace VariableOSC
{
    /// <summary>
    /// A variable's location expressed relative to a loaded module instead
    /// of as a raw address. Modules get relocated to a different base
    /// address every time a process starts (ASLR), but the offset from a
    /// module's base to a static/global variable - or to the first link in
    /// a pointer chain that eventually reaches a heap-allocated variable -
    /// stays constant across runs. Saving this instead of a raw address is
    /// what lets a variable survive a restart of the target process.
    ///
    /// Two shapes:
    ///  - Direct (IsPointerChain = false): the value itself lives at
    ///    ModuleName + BaseOffset. No dereference. This is what auto-detect
    ///    proposes for a static/global variable found inside a module.
    ///  - Pointer chain (IsPointerChain = true): ModuleName + BaseOffset is
    ///    itself a pointer. It's dereferenced, Offsets[0] is added, that's
    ///    dereferenced again, Offsets[1] is added, and so on - except the
    ///    last offset in the chain, which is added but NOT dereferenced,
    ///    since that final address is where the variable itself lives.
    ///    Built by hand (or from a chain found with another tool) when a
    ///    variable lives on the heap and a simple module+offset isn't
    ///    enough to find it again after a restart.
    /// </summary>
    public class PointerPath
    {
        public string ModuleName { get; set; } = "";

        public long BaseOffset { get; set; }

        public bool IsPointerChain { get; set; }

        public List<long> Offsets { get; set; } = new();
    }
}
